using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' is not available.")
    {
    }
}
