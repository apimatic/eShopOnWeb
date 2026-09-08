using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Thrown when the requested subscription plan cannot be subscribed to because it does
/// not exist or does not belong to the configured billing catalog.
/// </summary>
public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' is not available in the configured billing catalog.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
