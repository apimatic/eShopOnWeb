namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>A display name for the supplier.</summary>
    public string? Name { get; set; }

    /// <summary>The absolute URL of the supplier's product listing page.</summary>
    public string? ProductListingUrl { get; set; }
}
