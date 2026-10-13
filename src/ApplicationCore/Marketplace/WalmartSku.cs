using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Marketplace;

/// <summary>
/// Maps between an eShop catalog item id and the seller SKU it is sold under on Walmart
/// Marketplace. The SKU for catalog item {id} is <c>ESHOP-{id}</c>.
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    /// <summary>Builds the Walmart seller SKU for a given eShop catalog item id.</summary>
    public static string ForCatalogItem(int catalogItemId) => $"{Prefix}{catalogItemId}";

    /// <summary>
    /// Attempts to parse a Walmart seller SKU back into an eShop catalog item id. Returns
    /// <c>false</c> for any SKU that does not belong to the eShop catalog (e.g. an item listed on
    /// Walmart that has no eShop counterpart).
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
