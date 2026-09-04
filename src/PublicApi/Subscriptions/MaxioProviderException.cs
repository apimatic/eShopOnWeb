using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
