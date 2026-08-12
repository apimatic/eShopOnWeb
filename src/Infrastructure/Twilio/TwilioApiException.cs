using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when a Twilio API call returns a non-success status. Carries the provider's own
/// error model (status/code/message) from the spec's error response. Never carries the
/// destination number.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode httpStatus, int? twilioCode, string? twilioMessage)
        : base($"Twilio API call failed (HTTP {(int)httpStatus}{(twilioCode is not null ? $", code {twilioCode}" : string.Empty)}): {twilioMessage}")
    {
        HttpStatus = httpStatus;
        TwilioCode = twilioCode;
        TwilioMessage = twilioMessage;
    }

    public HttpStatusCode HttpStatus { get; }
    public int? TwilioCode { get; }
    public string? TwilioMessage { get; }
}
