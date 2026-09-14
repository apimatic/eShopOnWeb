using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException()
        : base("A subscription request for this plan is already in progress.")
    {
    }
}
