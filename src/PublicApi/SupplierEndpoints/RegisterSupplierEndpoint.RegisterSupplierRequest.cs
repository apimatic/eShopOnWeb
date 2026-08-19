namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>A name for the supplier.</summary>
    public string Name { get; set; }

    /// <summary>The URL of the supplier's product listing page that a sync will read.</summary>
    public string ProductListingUrl { get; set; }
}
