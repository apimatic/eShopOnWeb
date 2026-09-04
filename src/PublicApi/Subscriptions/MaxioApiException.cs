using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode)
        : base($"Maxio returned HTTP status {statusCode}.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
