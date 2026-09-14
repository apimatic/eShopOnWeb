using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(int statusCode, string title, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}
