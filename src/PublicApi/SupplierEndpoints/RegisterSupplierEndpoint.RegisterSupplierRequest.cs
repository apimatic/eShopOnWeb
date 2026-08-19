namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class RegisterSupplierRequest : BaseRequest
{
    /// <summary>A human-friendly name for the supplier.</summary>
    public string Name { get; set; }

    /// <summary>The absolute URL of the supplier's product listing page to read during a sync.</summary>
    public string ListingUrl { get; set; }
}
