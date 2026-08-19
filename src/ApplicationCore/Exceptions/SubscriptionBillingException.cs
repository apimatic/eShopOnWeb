using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.", 400)
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
