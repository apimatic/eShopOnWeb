using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        HttpStatusCode statusCode,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
