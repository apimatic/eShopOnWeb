using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Twilio API call returns an error response. Carries the HTTP status and
/// Twilio's own error code/message so callers can react, without ever carrying PII.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? twilioCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        TwilioCode = twilioCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? TwilioCode { get; }
}
