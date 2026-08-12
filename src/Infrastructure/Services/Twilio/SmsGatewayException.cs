using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when a call to the Twilio API fails. The message is built from the provider's error model
/// (code + description) and never contains the auth token.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, int? providerErrorCode = null) : base(message)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
