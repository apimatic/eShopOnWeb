using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Twilio;

/// <summary>
/// Raised when the Twilio API answers with a non-success status code.
/// Carries the provider's error payload (code/message) when one was returned.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? twilioErrorCode, string? twilioErrorMessage)
        : base($"Twilio API request failed with HTTP {(int)statusCode} ({statusCode})" +
               (twilioErrorCode.HasValue ? $", Twilio error {twilioErrorCode}: {twilioErrorMessage}" : string.Empty))
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
        TwilioErrorMessage = twilioErrorMessage;
    }

    public HttpStatusCode StatusCode { get; }
    public int? TwilioErrorCode { get; }
    public string? TwilioErrorMessage { get; }
}
