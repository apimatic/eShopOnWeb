using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested plan handle is not offered by the configured product family.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"No subscription plan with handle '{planHandle}' is available in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }

    public string ProductFamilyHandle { get; }
}
