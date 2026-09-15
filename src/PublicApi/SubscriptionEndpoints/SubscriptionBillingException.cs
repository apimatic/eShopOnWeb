using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
