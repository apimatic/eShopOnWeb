using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Thrown when a shopper requests a subscription plan that is not offered on the configured
/// Maxio product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available to subscribe to.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
