using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
    }
}

internal sealed class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message)
        : base(message)
    {
    }
}
