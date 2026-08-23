using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        HttpStatusCode statusCode,
        string title,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public HttpStatusCode StatusCode { get; }

    public string Title { get; }
}
