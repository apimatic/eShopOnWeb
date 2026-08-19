namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>Display name of the supplier.</summary>
    public string Name { get; set; }

    /// <summary>Absolute URL of the supplier's product listing page to sync from.</summary>
    public string ProductListingUrl { get; set; }
}
