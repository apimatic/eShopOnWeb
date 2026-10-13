using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.WalmartIntegration;

/// <summary>
/// Maps between an eShop catalog item id and its Walmart seller SKU. The SKU for catalog item
/// <c>N</c> is <c>ESHOP-N</c>.
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    public static string ForCatalogItem(int catalogItemId) =>
        Prefix + catalogItemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a seller SKU back to its catalog item id. Returns false for any SKU that is not of the
    /// form <c>ESHOP-{positive-integer}</c> (i.e. not an eShop SKU).
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
