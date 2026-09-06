using System;
using System.Net;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Translates provider failures into <see cref="SubscriptionBillingException"/>.
/// </summary>
/// <remarks>
/// This handles only untyped (<see cref="RawError"/>) and transport failures. Typed per-operation
/// error payloads must be read inside the operation's own catch block, where the concrete error
/// type is known — a shared helper can only reach the base <c>ApiError</c> surface and would
/// silently drop every typed body.
/// </remarks>
internal static class MaxioFailures
{
    private const int MaxProviderDetailLength = 500;

    public static SubscriptionBillingException NotConfigured(
        string message = "Subscription billing is not configured for this environment.") =>
        new(BillingFailureKind.NotConfigured, message);

    public static SubscriptionBillingException Rejected(string message, HttpStatusCode? providerStatusCode = null) =>
        new(BillingFailureKind.Rejected, message, providerStatusCode);

    public static SubscriptionBillingException NotFound(string message) =>
        new(BillingFailureKind.NotFound, message);

    /// <summary>
    /// Maps an untyped provider error. A 4xx is the caller's problem and keeps its status so it does
    /// not resurface as a retryable outage; anything else is treated as the provider being unwell.
    /// </summary>
    public static SubscriptionBillingException FromRawError(RawError error, string operation, Exception? inner = null)
    {
        var status = error.StatusCode;
        var code = (int)status;

        if (status == HttpStatusCode.NotFound)
        {
            return new SubscriptionBillingException(BillingFailureKind.NotFound,
                $"Maxio has no record for the requested resource ({operation}).", status, inner);
        }

        if (code >= 400 && code < 500)
        {
            var detail = ReadDetail(error);
            var message = detail is null
                ? $"Maxio rejected the request ({operation})."
                : $"Maxio rejected the request ({operation}): {detail}";

            return new SubscriptionBillingException(BillingFailureKind.Rejected, message, status, inner);
        }

        // Provider-side detail on a 5xx is not the caller's business and is often an HTML error page.
        return new SubscriptionBillingException(BillingFailureKind.Unavailable,
            $"Maxio returned an unexpected status while {operation} (HTTP {code}).", status, inner);
    }

    /// <summary>The provider could not be reached, or the call ran out of time.</summary>
    public static SubscriptionBillingException Unavailable(string operation, Exception inner) => new(
        BillingFailureKind.Unavailable,
        $"Maxio could not be reached while {operation}.",
        providerStatusCode: null,
        innerException: inner);

    /// <summary>
    /// A response arrived but could not be deserialised. On a success status this means the outcome
    /// is genuinely unknown; the message never carries serializer detail, which would leak
    /// System.Text.Json type and JSON-path information onto the wire.
    /// </summary>
    public static SubscriptionBillingException UnreadableResponse(string operation, Exception inner) => new(
        BillingFailureKind.UnreadableResponse,
        $"Maxio returned a response that could not be processed ({operation}).",
        providerStatusCode: null,
        innerException: inner);

    /// <summary>
    /// A rejection whose body did not match the SDK's generated error shape, so the SDK threw while
    /// building the error object and the HTTP status was lost with it. It is still a rejection —
    /// reporting it as a server fault would tell a caller to retry something that cannot succeed.
    /// </summary>
    public static SubscriptionBillingException UnreadableRejection(string operation, Exception inner) => new(
        BillingFailureKind.Rejected,
        $"Maxio rejected the request ({operation}), and the reason could not be read.",
        providerStatusCode: null,
        innerException: inner);

    /// <summary>
    /// A write whose re-send was refused. The single send that was allowed may have taken effect, so
    /// callers reconcile by re-reading provider state instead of reporting a definite failure.
    /// </summary>
    public static SubscriptionBillingException UnknownWriteOutcome(string operation, Exception inner) => new(
        BillingFailureKind.Unavailable,
        $"Maxio did not confirm the request ({operation}); its outcome could not be established.",
        providerStatusCode: null,
        innerException: inner);

    public static bool IsTransportFailure(Exception exception) =>
        exception is System.Net.Http.HttpRequestException
        || exception is System.IO.IOException
        || exception is System.Net.Sockets.SocketException;

    public static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value!.Length <= MaxProviderDetailLength ? value.Trim()
        : value.Substring(0, MaxProviderDetailLength).Trim() + "…";

    private static string? ReadDetail(RawError error)
    {
        try
        {
            return Truncate(error.ReadAsString());
        }
        catch (Exception)
        {
            // A body we cannot even read as text adds nothing to the caller-facing message.
            return null;
        }
    }
}
