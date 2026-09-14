namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle does not exist in the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"Subscription plan '{planHandle}' was not found in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }
    public string ProductFamilyHandle { get; }
}
