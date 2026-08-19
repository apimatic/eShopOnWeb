namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>A human-friendly name for the supplier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The URL of the supplier's product listing page.</summary>
    public string ProductListingUrl { get; set; } = string.Empty;
}
