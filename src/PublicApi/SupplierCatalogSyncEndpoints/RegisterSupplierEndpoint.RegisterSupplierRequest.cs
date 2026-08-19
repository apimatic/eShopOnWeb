namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

/// <summary>
/// Registers a supplier: a display name and the URL of its public product listing page.
/// </summary>
public class RegisterSupplierRequest
{
    public string? Name { get; set; }

    /// <summary>The URL of the supplier's product listing page to sync from.</summary>
    public string? ProductListingUrl { get; set; }
}
