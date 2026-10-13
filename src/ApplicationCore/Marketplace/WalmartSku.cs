using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Marketplace;

/// <summary>
/// Maps between an eShop catalog item id and the seller SKU it is sold under on Walmart
/// Marketplace. The SKU for catalog item N is <c>ESHOP-N</c> (e.g. item 7 is <c>ESHOP-7</c>).
/// This is the single source of truth for that convention.
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    /// <summary>Returns the Walmart seller SKU for the given eShop catalog item id.</summary>
    public static string ForCatalogItem(int catalogItemId) =>
        Prefix + catalogItemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Attempts to recover the eShop catalog item id from a Walmart seller SKU. Returns
    /// <c>false</c> for SKUs that do not follow the <c>ESHOP-{id}</c> convention (for example a
    /// SKU that exists only on the Walmart side).
    /// </summary>
    public static bool TryGetCatalogItemId(string? sku, out int catalogItemId)
    {
        catalogItemId = 0;
        if (string.IsNullOrEmpty(sku) || !sku.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idPart = sku.Substring(Prefix.Length);
        return int.TryParse(idPart, NumberStyles.None, CultureInfo.InvariantCulture, out catalogItemId);
    }
}
