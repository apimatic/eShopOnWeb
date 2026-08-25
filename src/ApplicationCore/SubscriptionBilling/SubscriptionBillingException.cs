using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.BadGateway,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
