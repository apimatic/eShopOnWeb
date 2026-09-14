using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"A subscription plan with handle '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
