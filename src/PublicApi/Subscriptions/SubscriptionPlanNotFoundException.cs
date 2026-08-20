using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle)
        : base($"Subscription plan '{handle}' is not available.")
    {
    }
}
