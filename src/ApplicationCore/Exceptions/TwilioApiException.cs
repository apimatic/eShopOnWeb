using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a Twilio provider call fails. Carries the provider's error code (when supplied) so
/// callers can react without re-parsing the response. The message never contains a phone number,
/// a message body, or the auth token.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
