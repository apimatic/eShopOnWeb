using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Raised when a Twilio messaging-API call is rejected. Carries only the HTTP status and Twilio's
/// numeric error code — never the destination number or any request content — so it is safe to log.
/// </summary>
public class TwilioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? TwilioErrorCode { get; }

    public TwilioApiException(HttpStatusCode statusCode, int? twilioErrorCode)
        : base($"Twilio API call failed with HTTP {(int)statusCode} (Twilio error code: {twilioErrorCode?.ToString() ?? "n/a"}).")
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
    }
}
