using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Raised when the shopper requests a plan that is not offered by the configured Maxio
/// product family. Maps to HTTP 404.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when the subscribing shopper could not be identified from the authenticated token.
/// Maps to HTTP 401.
/// </summary>
public class ShopperNotFoundException : Exception
{
    public ShopperNotFoundException(string userName)
        : base($"The authenticated shopper '{userName}' does not exist in the storefront identity store.")
    {
    }
}
