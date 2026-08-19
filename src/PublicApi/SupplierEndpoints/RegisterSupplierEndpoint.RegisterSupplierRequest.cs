namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>Human-readable name of the supplier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute URL of the supplier's product listing page.</summary>
    public string ProductListingUrl { get; set; } = string.Empty;
}
