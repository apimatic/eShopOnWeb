using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Raised when a Twilio messaging call fails. The message is deliberately kept to the HTTP
/// status and the provider's error <c>code</c> only — never the provider's error text, which
/// can echo the destination phone number.
/// </summary>
public class TwilioMessagingException : Exception
{
    public int HttpStatusCode { get; }
    public string? ProviderErrorCode { get; }

    public TwilioMessagingException(int httpStatusCode, string? providerErrorCode)
        : base($"Twilio messaging call failed (HTTP {httpStatusCode}, code {providerErrorCode ?? "n/a"}).")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
