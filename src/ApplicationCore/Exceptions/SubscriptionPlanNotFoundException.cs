using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }
}
