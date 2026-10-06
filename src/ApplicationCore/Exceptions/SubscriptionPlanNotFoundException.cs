using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' was not found in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
