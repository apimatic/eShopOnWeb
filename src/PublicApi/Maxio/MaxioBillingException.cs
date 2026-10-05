using System;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type the Maxio integration boundary raises; callers (the
/// subscription endpoints) map its kind and provider status onto HTTP responses.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public enum FailureKind
    {
        /// <summary>The provider rejected the caller's request (4xx) — safe to surface.</summary>
        RequestRejected,

        /// <summary>Maxio is unavailable/overloaded or our credentials are bad — not the caller's fault.</summary>
        ProviderUnavailable,

        /// <summary>A write may or may not have landed; outcome not settled (connection failed after the write).</summary>
        UnknownOutcome
    }

    public FailureKind Kind { get; }

    /// <summary>The provider's HTTP status when the server answered; null for transport failures.</summary>
    public int? ProviderStatusCode { get; }

    public MaxioBillingException(FailureKind kind, string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>
    /// Reads one typed error payload through its accessors (in the operation's catch, where the
    /// concrete error type is known) into a caller-safe message. Covers every declared accessor,
    /// with the raw fallback last.
    /// </summary>
    internal static string DescribeCreateSubscriptionError(CreateSubscriptionError error, string rawBody)
    {
        if (error.TryGetErrorListResponse1(out var list))
        {
            return string.Join("; ", list.Errors);
        }
        return Truncate(rawBody);
    }

    internal static string DescribeCreateCustomerError(CreateCustomerError error, string rawBody)
    {
        if (error.TryGetCustomerErrorResponse1(out var response) && response.Errors is not null)
        {
            var errors = response.Errors;
            if (errors.TryGetListOfString(out var listOfStrings))
            {
                return string.Join("; ", listOfStrings);
            }
            if (errors.TryGetCustomerError(out _))
            {
                // Field-keyed error map — log-safe summary only, details stay server-side.
                return "One or more customer fields were rejected by Maxio.";
            }
        }
        return Truncate(rawBody);
    }

    /// <summary>
    /// Converts the exceptions a Maxio call can raise (the SdkException family and the
    /// two ApiException cases) into <see cref="MaxioBillingException"/>. UnknownOutcome
    /// writes are settled by the caller before this runs.
    /// </summary>
    internal static MaxioBillingException FromSdkException(SdkException ex)
    {
        return ex switch
        {
            ApiException<RawError> api => new MaxioBillingException(
                api.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError
                    ? FailureKind.RequestRejected
                    : FailureKind.ProviderUnavailable,
                $"Maxio call failed with HTTP {(int)api.StatusCode}.",
                (int)api.StatusCode,
                ex),
            ResponseDeserializationException deser => new MaxioBillingException(
                deser.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError
                    ? FailureKind.RequestRejected
                    : FailureKind.ProviderUnavailable,
                "Maxio returned a response that could not be processed.",
                (int)deser.StatusCode,
                ex),
            SdkTimeoutException timeout => new MaxioBillingException(
                FailureKind.ProviderUnavailable,
                $"Maxio did not answer within {timeout.Timeout}.",
                null,
                ex),
            SdkConnectionException => new MaxioBillingException(
                FailureKind.ProviderUnavailable,
                "Maxio could not be reached.",
                null,
                ex),
            AuthSchemeException => new MaxioBillingException(
                FailureKind.ProviderUnavailable,
                "Maxio credentials could not be applied.",
                null,
                ex),
            _ => new MaxioBillingException(FailureKind.ProviderUnavailable, "Maxio call failed.", null, ex)
        };
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Maxio rejected the request.";
        }
        return text.Length <= 500 ? text : text[..500] + "…";
    }
}