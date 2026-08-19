namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>Human-friendly supplier name.</summary>
    public string Name { get; set; }

    /// <summary>The URL of the supplier's product listing page.</summary>
    public string ProductListingUrl { get; set; }
}
