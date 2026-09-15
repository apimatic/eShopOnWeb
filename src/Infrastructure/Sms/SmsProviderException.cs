using System;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when the SMS provider returns an error for an operation that is expected to succeed
/// (fetch, cancel, redact, list). Carries the provider's numeric error code. The message never
/// contains the shopper's phone number.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? providerErrorCode, string? providerMessage)
        : base(message)
    {
        ProviderErrorCode = providerErrorCode;
        ProviderMessage = providerMessage;
    }

    public int? ProviderErrorCode { get; }
    public string? ProviderMessage { get; }
}
