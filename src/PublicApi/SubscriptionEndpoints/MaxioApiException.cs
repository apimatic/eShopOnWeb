using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation)
        : base($"Maxio could not complete {operation}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
