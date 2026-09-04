using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioServiceException : Exception
{
    public MaxioServiceException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
