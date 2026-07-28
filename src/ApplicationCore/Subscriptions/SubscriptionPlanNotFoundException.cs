using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Thrown when a shopper attempts to subscribe to a plan handle that is not offered by the
/// configured product family. Surfaces to the API as a 400/404 rather than a 500.
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
