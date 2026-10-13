using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>
/// Maps between an eShop catalog item id and the seller SKU it is sold under on Walmart.
/// The SKU for catalog item <c>N</c> is <c>ESHOP-N</c>.
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    /// <summary>Returns the Walmart seller SKU for a catalog item id (e.g. 7 → <c>ESHOP-7</c>).</summary>
    public static string ForCatalogItem(int catalogItemId) =>
        Prefix + catalogItemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Attempts to parse a Walmart seller SKU back into its eShop catalog item id. Returns false for
    /// SKUs that do not follow the <c>ESHOP-{id}</c> convention (i.e. SKUs that are not an eShop item).
    /// </summary>
    public static bool TryParseCatalogItemId(string? sku, out int catalogItemId)
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
