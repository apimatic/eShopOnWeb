using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Raised when the Twilio API rejects a request outright (non-success HTTP status). Carries the
/// provider's error model (HTTP status, Twilio <c>code</c>, and <c>message</c>) as described by the
/// spec's error responses. The message is deliberately free of any recipient phone number.
/// </summary>
public class TwilioApiException : Exception
{
    public int HttpStatus { get; }
    public int? TwilioCode { get; }

    public TwilioApiException(int httpStatus, int? twilioCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        TwilioCode = twilioCode;
    }
}
