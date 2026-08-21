using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, string? providerCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public string? ProviderCode { get; }
}
