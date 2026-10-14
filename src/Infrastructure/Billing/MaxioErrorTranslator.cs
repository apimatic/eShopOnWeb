using System.Net;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The one mapping from Maxio SDK failures to caller-facing <see cref="BillingProviderException"/>s.
/// Case A (typed) error bodies are read at each call site, where the concrete error type is known; this class
/// covers the status-only and transport arms shared by every operation.
/// </summary>
internal static class MaxioErrorTranslator
{
    public const string UnavailableMessage = "The billing service is temporarily unavailable. Please try again shortly.";
    public const string FailedMessage = "The billing service could not process the request.";

    public static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    /// <summary>Maps a status the billing system answered with to the status our caller sees.</summary>
    public static BillingProviderException FromStatus(HttpStatusCode providerStatus, SdkException inner)
    {
        var status = (int)providerStatus;
        return status switch
        {
            // Our credentials or our quota: the caller did nothing wrong and cannot fix it.
            401 or 403 => new BillingProviderException(502, FailedMessage, status, inner),
            429 => new BillingProviderException(503, UnavailableMessage, status, inner),
            // Every caller-supplied value is validated before we call Maxio, so any other provider status is
            // an integration or provider fault, not something the caller can act on.
            _ => new BillingProviderException(502, FailedMessage, status, inner),
        };
    }

    /// <summary>
    /// Translates the SDK failure kinds that are not operation-specific. Only call for operations whose outcome
    /// is not ambiguous (reads), or after an ambiguous write has been settled.
    /// </summary>
    public static BillingProviderException Translate(SdkException exception) => exception switch
    {
        ApiException<RawError> api => FromStatus(api.StatusCode, api),
        ResponseDeserializationException unreadable when IsSuccess(unreadable.StatusCode) =>
            new BillingProviderException(502, FailedMessage, (int)unreadable.StatusCode, unreadable),
        ApiException api => FromStatus(api.StatusCode, api),
        SdkTimeoutException timeout => new BillingProviderException(504, UnavailableMessage, null, timeout),
        SdkConnectionException connection => new BillingProviderException(503, UnavailableMessage, null, connection),
        AuthSchemeException auth => new BillingProviderException(502, FailedMessage, null, auth),
        _ => new BillingProviderException(502, FailedMessage, null, exception),
    };
}
