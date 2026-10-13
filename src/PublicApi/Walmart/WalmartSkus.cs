using System;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Maps between an eShop catalog item id and the seller SKU it is sold under on Walmart.
/// The SKU for catalog item <c>N</c> is <c>ESHOP-{N}</c>.
/// </summary>
public static class WalmartSkus
{
    public const string Prefix = "ESHOP-";

    public static string ForCatalogItem(int catalogItemId) => $"{Prefix}{catalogItemId}";

    /// <summary>
    /// True when <paramref name="sku"/> is one of this shop's SKUs (<c>ESHOP-{id}</c> with a positive id),
    /// yielding the catalog item id. False for any SKU that does not belong to this shop.
    /// </summary>
    public static bool TryGetCatalogItemId(string? sku, out int catalogItemId)
    {
        catalogItemId = 0;
        if (string.IsNullOrWhiteSpace(sku)) return false;
        if (!sku.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        return int.TryParse(sku.AsSpan(Prefix.Length), out catalogItemId) && catalogItemId > 0;
    }
}
