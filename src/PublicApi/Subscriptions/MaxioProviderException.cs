using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(string publicMessage, int? statusCode = null, Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
