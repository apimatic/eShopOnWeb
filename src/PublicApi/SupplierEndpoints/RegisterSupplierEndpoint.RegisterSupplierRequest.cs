namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>Display name of the supplier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL of the supplier's product listing page to sync from.</summary>
    public string ListingUrl { get; set; } = string.Empty;
}
