namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle does not exist in the configured product family, or is archived.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
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
