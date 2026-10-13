using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Marketplace;

/// <summary>
/// Maps between an eShop catalog item id and the seller SKU it is sold under on Walmart.
/// The SKU for catalog item {id} is <c>ESHOP-{id}</c> (catalog item 7 → <c>ESHOP-7</c>).
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    public static string ForCatalogItem(int catalogItemId) => $"{Prefix}{catalogItemId}";

    /// <summary>
    /// Recovers the catalog item id from a seller SKU. Returns false for a SKU that is not one
    /// this application issued (so a SKU that exists only on Walmart lines up against no eShop item).
    /// </summary>
    public static bool TryGetCatalogItemId(string? sku, out int catalogItemId)
    {
        catalogItemId = 0;
        if (string.IsNullOrEmpty(sku) || !sku.StartsWith(Prefix, System.StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(sku.Substring(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out catalogItemId);
    }
}
