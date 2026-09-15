using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when a Twilio messaging API call fails at the HTTP level. Deliberately carries only the
/// HTTP status and Twilio error code — never the raw provider message, which can echo the
/// destination number back and must never reach a log.
/// </summary>
public class TwilioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? TwilioCode { get; }

    public TwilioApiException(string operation, HttpStatusCode statusCode, int? twilioCode)
        : base($"Twilio {operation} failed with HTTP {(int)statusCode} (twilio code {twilioCode?.ToString() ?? "n/a"}).")
    {
        StatusCode = statusCode;
        TwilioCode = twilioCode;
    }
}
