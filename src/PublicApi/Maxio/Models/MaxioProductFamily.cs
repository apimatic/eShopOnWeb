namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A Maxio Product Family (container for products/components/coupons).
/// Mirrors the subset of Product-Family.yaml used by the integration.
/// </summary>
public class MaxioProductFamily
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }
}
