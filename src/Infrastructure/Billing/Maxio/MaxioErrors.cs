using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AdvancedBilling.Standard.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Turns Advanced Billing failures into the domain's billing exceptions.
/// </summary>
/// <remarks>
/// The split that matters to a caller is "your request was wrong" versus "the billing system is having a
/// moment" versus "this deployment is misconfigured", so the HTTP status drives the mapping. Nothing here
/// echoes the request back — the SDK's exception carries the <c>Authorization</c> header on it, and that
/// must never reach a log or a response body.
/// </remarks>
internal static class MaxioErrors
{
    public static int? StatusCodeOf(ApiException exception) => exception.HttpContext?.Response?.StatusCode;

    /// <summary>
    /// Extracts the human-readable validation messages Advanced Billing returned, preferring the SDK's
    /// typed shapes and falling back to the raw body.
    /// </summary>
    public static IReadOnlyList<string> MessagesOf(ApiException exception)
    {
        switch (exception)
        {
            case ErrorListResponseException { Errors: { Count: > 0 } errors }:
                return errors;

            case CustomerErrorResponseException { Errors: not null } customerError:
                var matched = customerError.Errors.MatchSome(
                    customerErrorCase => customerErrorCase?.Customer,
                    listOfStringCase => listOfStringCase is null ? null : string.Join("; ", listOfStringCase));

                if (!string.IsNullOrWhiteSpace(matched))
                {
                    return new[] { matched! };
                }

                break;
        }

        var body = exception.HttpContext?.Response?.Body;
        return string.IsNullOrWhiteSpace(body) ? Array.Empty<string>() : new[] { body! };
    }

    /// <summary>
    /// Maps <paramref name="exception"/> onto the domain exception that best describes what the caller
    /// should do about it. <paramref name="operation"/> names what was being attempted, for the message.
    /// </summary>
    public static SubscriptionBillingException Translate(ApiException exception, string operation)
    {
        var status = StatusCodeOf(exception);
        var detail = Describe(exception);

        return status switch
        {
            401 or 403 => new SubscriptionBillingConfigurationException(
                $"Advanced Billing rejected the configured credentials while {operation}. " +
                $"Check {MaxioSettings.SectionName}:ApiKey and {MaxioSettings.SectionName}:Subdomain.",
                exception),

            400 or 422 => new SubscriptionBillingRejectedException(
                $"Advanced Billing rejected the request while {operation}: {detail}",
                exception),

            429 => new SubscriptionBillingUnavailableException(
                $"Advanced Billing is rate limiting this site; {operation} was throttled. Try again shortly.",
                exception),

            _ => new SubscriptionBillingUnavailableException(
                $"Advanced Billing failed while {operation}" +
                (status is null ? "." : $" (HTTP {status}).") +
                (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}"),
                exception),
        };
    }

    /// <summary>
    /// Maps a failure that never produced an HTTP response — DNS, TLS, connection resets, timeouts.
    /// </summary>
    public static SubscriptionBillingException TranslateTransport(Exception exception, string operation) =>
        new SubscriptionBillingUnavailableException(
            $"Could not reach Advanced Billing while {operation}: {exception.Message}",
            exception);

    /// <summary>True for the exception types that mean "the call never completed cleanly".</summary>
    public static bool IsTransport(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException;

    private static string Describe(ApiException exception)
    {
        var messages = MessagesOf(exception).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
        return messages.Length == 0 ? exception.Message : string.Join("; ", messages);
    }
}
