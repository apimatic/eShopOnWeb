namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle does not exist in the configured product family, so it is not something
/// a shopper may subscribe to. Surfaced to callers as <c>400 Bad Request</c>.
/// </summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"No plan with handle '{planHandle}' is available in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }

    public string ProductFamilyHandle { get; }
}
