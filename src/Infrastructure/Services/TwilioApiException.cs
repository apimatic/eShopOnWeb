using System;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Raised when the Twilio API returns an error response. Carries Twilio's own error code so callers
/// can distinguish (for example) an invalid destination from a transport failure. The message never
/// contains the destination number or the auth token.
/// </summary>
public class TwilioApiException : Exception
{
    public int HttpStatus { get; }
    public int? TwilioCode { get; }

    public TwilioApiException(int httpStatus, int? twilioCode, string message) : base(message)
    {
        HttpStatus = httpStatus;
        TwilioCode = twilioCode;
    }
}
