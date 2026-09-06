using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Shared translation of the failures that carry no operation-specific payload - raw provider errors,
/// transport failures and unreadable bodies - into <see cref="SubscriptionBillingException"/>.
/// </summary>
/// <remarks>
/// Typed 422 payloads are deliberately <em>not</em> handled here. Their accessors live on the concrete
/// per-operation error type, so reading them from a helper typed as the base class would silently find
/// nothing; each call site reads its own typed accessors inside its own catch block and calls in here only
/// for the raw fallback.
/// </remarks>
internal static class MaxioFailureTranslation
{
    /// <summary>Maps a provider HTTP status onto the failure kind this application reports.</summary>
    public static SubscriptionBillingFailure Classify(int statusCode) => statusCode switch
    {
        400 or 422 => SubscriptionBillingFailure.InvalidRequest,
        404 => SubscriptionBillingFailure.NotFound,
        401 or 403 => SubscriptionBillingFailure.ProviderMisconfigured,
        _ => SubscriptionBillingFailure.ProviderUnavailable
    };

    /// <summary>
    /// Translates a raw provider error. The provider's body is logged, never returned: only the caller-safe
    /// message below goes on the wire.
    /// </summary>
    public static SubscriptionBillingException FromRawError(ILogger logger, RawError raw, string operation)
    {
        var statusCode = (int)raw.StatusCode;
        var failure = Classify(statusCode);

        logger.Log(
            failure == SubscriptionBillingFailure.NotFound ? LogLevel.Debug : LogLevel.Error,
            "Maxio operation {Operation} failed with HTTP {StatusCode}. Body: {Body}",
            operation,
            statusCode,
            SafeBody(raw));

        return new SubscriptionBillingException(failure, MessageFor(failure, operation), statusCode);
    }

    /// <summary>
    /// Translates a connection failure, timeout or blocked re-send. These never reach an
    /// <c>SdkException</c> catch, so every call site must guard for them too.
    /// </summary>
    public static SubscriptionBillingException FromTransport(
        ILogger logger,
        Exception exception,
        string operation,
        bool isWrite)
    {
        if (exception is DuplicateSendBlockedException)
        {
            logger.LogError(
                exception,
                "Maxio write {Operation} was not re-sent after a transport failure; its outcome is unknown.",
                operation);

            return new SubscriptionBillingException(
                SubscriptionBillingFailure.OutcomeUnknown,
                "The billing provider could not confirm whether the request took effect. Re-read your subscriptions before retrying.",
                providerStatusCode: null,
                exception);
        }

        logger.LogError(exception, "Maxio operation {Operation} failed before a response was received.", operation);

        // A write that failed on the way out may still have been received, so its outcome is unknown
        // rather than failed. A read simply did not happen.
        return isWrite
            ? new SubscriptionBillingException(
                SubscriptionBillingFailure.OutcomeUnknown,
                "The billing provider could not confirm whether the request took effect. Re-read your subscriptions before retrying.",
                providerStatusCode: null,
                exception)
            : new SubscriptionBillingException(
                SubscriptionBillingFailure.ProviderUnavailable,
                "The billing provider is currently unavailable. Please try again shortly.",
                providerStatusCode: null,
                exception);
    }

    /// <summary>
    /// Translates a payload that could not be deserialized. On a success status this means the outcome is
    /// unknown; it must never be reported as a domain absence, because "I could not read the answer" is
    /// not "the provider said no".
    /// </summary>
    public static SubscriptionBillingException FromUnreadablePayload(
        ILogger logger,
        JsonException exception,
        string operation)
    {
        logger.LogError(
            exception,
            "Maxio operation {Operation} returned a payload that could not be read. Note that when this happens on an "
            + "error response the provider's status is lost with it, so the failure below is reported as unknown rather than as a rejection.",
            operation);

        return new SubscriptionBillingException(
            SubscriptionBillingFailure.ProviderResponseUnreadable,
            "The billing provider returned a response that could not be processed.",
            providerStatusCode: null,
            exception);
    }

    /// <summary>True for the exceptions that reach us instead of, not as, an SDK error.</summary>
    public static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or DuplicateSendBlockedException;

    public static string MessageFor(SubscriptionBillingFailure failure, string operation) => failure switch
    {
        SubscriptionBillingFailure.InvalidRequest =>
            "The billing provider rejected the request as invalid.",
        SubscriptionBillingFailure.NotFound =>
            "The requested billing record was not found.",
        SubscriptionBillingFailure.ProviderMisconfigured =>
            "Subscription billing is not correctly configured for this deployment.",
        _ => "The billing provider is currently unavailable. Please try again shortly."
    };

    /// <summary>
    /// Reads a raw body for logging. <see cref="RawError"/> buffers it, so this is safe to call after the
    /// response is disposed - but the body of an untyped error is frequently not JSON, so it is read as
    /// text and never parsed here.
    /// </summary>
    public static string SafeBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? "(empty)" : Truncate(body, 2000);
        }
        catch (Exception ex)
        {
            return $"(unreadable: {ex.GetType().Name})";
        }
    }

    public static bool IsNotFound(RawError raw) => raw.StatusCode == HttpStatusCode.NotFound;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
}
