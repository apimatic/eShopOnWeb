using System;

namespace Microsoft.eShopWeb.Infrastructure.Notifications.Twilio;

/// <summary>
/// Raised when a Twilio API call returns an error, carrying the provider's own error code and
/// message (per the spec's error model). The offending phone number is never included.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? providerCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }

    public int HttpStatus { get; }
    public int? ProviderCode { get; }
}
