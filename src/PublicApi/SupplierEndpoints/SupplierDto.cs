using System;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

public class SupplierDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProductListingUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
