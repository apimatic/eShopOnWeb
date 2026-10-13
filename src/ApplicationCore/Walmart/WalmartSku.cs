using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>
/// The seller SKU under which an eShop catalog item is offered on Walmart Marketplace.
/// Catalog item 7 is <c>ESHOP-7</c>.
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    public static string ForCatalogItem(int catalogItemId) => $"{Prefix}{catalogItemId}";

    /// <summary>
    /// Parses the catalog item id out of a seller SKU of the form <c>ESHOP-{id}</c>.
    /// Returns false for any SKU that does not match (e.g. a SKU that exists only on Walmart).
    /// </summary>
    public static bool TryGetCatalogItemId(string? sku, out int catalogItemId)
    {
        catalogItemId = 0;
        if (string.IsNullOrEmpty(sku) || !sku.StartsWith(Prefix, System.StringComparison.Ordinal))
        {
            return false;
        }

        var idPart = sku.Substring(Prefix.Length);
        return int.TryParse(idPart, NumberStyles.None, CultureInfo.InvariantCulture, out catalogItemId)
            && catalogItemId > 0;
    }
}
