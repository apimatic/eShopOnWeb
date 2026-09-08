using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription plan (billing product) referenced by the caller does not exist
/// or is not available for subscription.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"A subscription plan with handle '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }
}
