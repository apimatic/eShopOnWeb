namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle is not offered by the configured subscription catalog.
/// </summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle, string? productFamilyHandle)
        : base($"No subscription plan with handle '{planHandle}' is offered" +
               (string.IsNullOrWhiteSpace(productFamilyHandle) ? "." : $" by product family '{productFamilyHandle}'."))
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }

    public string? ProductFamilyHandle { get; }
}
