using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        string message,
        HttpStatusCode statusCode,
        bool providerUnavailable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderUnavailable = providerUnavailable;
    }

    public HttpStatusCode StatusCode { get; }
    public bool ProviderUnavailable { get; }
}
