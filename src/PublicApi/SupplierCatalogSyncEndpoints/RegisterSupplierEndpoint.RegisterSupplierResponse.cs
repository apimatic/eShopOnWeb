using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierCatalogSyncEndpoints;

public class RegisterSupplierResponse
{
    /// <summary>The identifier of the newly registered supplier.</summary>
    public Guid SupplierId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ProductListingUrl { get; set; } = string.Empty;
}
