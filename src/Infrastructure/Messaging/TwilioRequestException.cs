using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioRequestException : Exception
{
    public TwilioRequestException(int statusCode, string message, int? providerCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
