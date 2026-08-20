using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.")
    {
    }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("A subscription enrollment for this plan is already in progress.")
    {
    }
}
