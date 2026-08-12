using System;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Raised when a Twilio API call fails at the transport/contract level (the provider could not be
/// reached, or returned an error status). The <see cref="Message"/> is scrubbed of phone numbers so
/// it is safe to store and log.
/// </summary>
public class TwilioApiException : Exception
{
    public int? HttpStatus { get; }
    public int? ProviderCode { get; }

    public TwilioApiException(string message, int? httpStatus = null, int? providerCode = null)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }
}
