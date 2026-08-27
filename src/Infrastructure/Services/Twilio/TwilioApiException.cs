using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>A non-success response from the provider's API. Never carries secrets or phone numbers.</summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? providerErrorCode, string providerMessage)
        : base($"Messaging provider request failed with HTTP {statusCode}" +
               (providerErrorCode.HasValue ? $" (provider error {providerErrorCode})" : string.Empty) +
               $": {providerMessage}")
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int StatusCode { get; }
    public int? ProviderErrorCode { get; }
}
