using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string operation, int statusCode)
        : base($"Maxio could not complete {operation} (HTTP {statusCode}).")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
