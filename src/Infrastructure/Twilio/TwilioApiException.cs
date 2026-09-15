using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? providerCode)
        : base($"Twilio request failed with HTTP {statusCode}" + (providerCode is null ? "." : $" (code {providerCode})."))
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
