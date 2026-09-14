using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}
