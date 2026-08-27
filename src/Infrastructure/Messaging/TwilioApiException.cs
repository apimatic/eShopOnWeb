using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// An error response from the Twilio API. Carries the provider's own error model
/// (status / code / message) — never credentials.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? twilioErrorCode, string message)
        : base($"Twilio API error {(int)statusCode} (code {twilioErrorCode?.ToString() ?? "n/a"}): {message}")
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? TwilioErrorCode { get; }
}
