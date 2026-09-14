using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller asked to subscribe to a plan handle that is not on offer in the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle, string availablePlanHandles)
        : base($"Subscription plan '{planHandle}' was not found. Available plans: {availablePlanHandles}.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
