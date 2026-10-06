using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription plan handle does not exist in the billing catalog.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}