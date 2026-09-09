using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when the requested subscription plan (Maxio product handle) does not exist.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }
}
