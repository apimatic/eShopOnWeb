using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the shopper asks to subscribe to a plan that does not exist in the
/// configured Maxio product family. Maps to HTTP 404.
/// </summary>
public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan '{planHandle}' is available in the configured Maxio catalog.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when the configured Maxio product family cannot be found on the site.
/// This is a deployment/configuration problem, not a client error.
/// </summary>
public sealed class MaxioProductFamilyNotFoundException : Exception
{
    public MaxioProductFamilyNotFoundException(string familyHandle)
        : base($"The configured Maxio product family '{familyHandle}' was not found on this site. " +
               "Verify the Maxio:ProductFamilyHandle setting (from MAXIO_DEFAULT_PRODUCT_FAMILY).")
    {
        FamilyHandle = familyHandle;
    }

    public string FamilyHandle { get; }
}
