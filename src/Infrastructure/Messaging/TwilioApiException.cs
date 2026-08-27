using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// A non-success response from the Twilio API. Carries the provider's error code
/// and HTTP status only; provider error messages can embed destination numbers,
/// so the raw body is deliberately not surfaced for logging.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? twilioErrorCode, string operation)
        : base($"Twilio {operation} failed with HTTP {(int)statusCode} (Twilio error code: {twilioErrorCode?.ToString() ?? "n/a"}).")
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
        Operation = operation;
    }

    public HttpStatusCode StatusCode { get; }
    public int? TwilioErrorCode { get; }
    public string Operation { get; }
}
