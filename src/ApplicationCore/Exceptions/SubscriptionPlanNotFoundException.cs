using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
