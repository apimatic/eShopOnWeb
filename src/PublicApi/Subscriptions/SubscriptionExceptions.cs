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
        : base("Subscription enrollment is already in progress. Retry this request shortly.")
    {
    }
}

public sealed class SubscriptionUserNotFoundException : Exception
{
    public SubscriptionUserNotFoundException()
        : base("The authenticated user no longer exists.")
    {
    }
}
