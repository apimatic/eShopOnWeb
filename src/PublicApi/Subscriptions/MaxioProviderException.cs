using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
