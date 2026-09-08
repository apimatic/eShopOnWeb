using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Raised when a shopper tries to subscribe to a plan handle that is not offered by the
/// configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
