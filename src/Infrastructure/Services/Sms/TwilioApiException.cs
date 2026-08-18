using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Raised when a Twilio API call fails. Carries the provider's error code so callers can record it.
/// The message deliberately avoids any shopper phone number or message body.
/// </summary>
public class TwilioApiException : Exception
{
    public int? StatusCode { get; }
    public int? ProviderErrorCode { get; }
    public string? ProviderErrorMessage { get; }

    public TwilioApiException(string message, int? statusCode = null, int? providerErrorCode = null, string? providerErrorMessage = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
    }
}
