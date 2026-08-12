using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when the Twilio API returns an error response. Carries the HTTP status and, when present,
/// the provider's own error code and message (from Twilio's error model). Never includes the auth
/// token or a recipient number.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? providerErrorCode, string message)
        : base($"Twilio API returned {(int)statusCode} ({statusCode}){(providerErrorCode.HasValue ? $", error {providerErrorCode}" : string.Empty)}: {message}")
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? ProviderErrorCode { get; }
}
