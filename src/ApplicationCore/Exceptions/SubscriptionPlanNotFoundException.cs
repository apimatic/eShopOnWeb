using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription plan (Maxio product handle) cannot be found among the
/// subscribable plans of the configured Maxio product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found among the available plans.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
