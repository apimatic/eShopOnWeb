using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

/// <summary>
/// A failed Twilio API call. Deliberately carries only the HTTP status and Twilio's numeric error
/// code — never the raw response body, which for messaging errors can echo the destination number.
/// </summary>
public class TwilioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? TwilioErrorCode { get; }

    public TwilioApiException(HttpStatusCode statusCode, int? twilioErrorCode)
        : base($"Twilio API request failed with HTTP {(int)statusCode} (Twilio code {twilioErrorCode?.ToString() ?? "n/a"}).")
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
    }
}
