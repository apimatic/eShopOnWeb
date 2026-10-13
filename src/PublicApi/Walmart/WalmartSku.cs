using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Translates between an eShop catalog item id and its Walmart seller SKU (<c>ESHOP-{id}</c>).
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    /// <summary>The seller SKU for a catalog item id (e.g. 7 → <c>ESHOP-7</c>).</summary>
    public static string ForCatalogItem(int catalogItemId) =>
        Prefix + catalogItemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a seller SKU back to its catalog item id. Returns true only for a well-formed
    /// <c>ESHOP-{id}</c> SKU whose id is a non-negative integer.
    /// </summary>
    public static bool TryGetCatalogItemId(string? sku, out int catalogItemId)
    {
        catalogItemId = 0;
        if (string.IsNullOrEmpty(sku) || !sku!.StartsWith(Prefix, System.StringComparison.Ordinal))
        {
            return false;
        }

        var idPart = sku.Substring(Prefix.Length);
        return int.TryParse(idPart, NumberStyles.None, CultureInfo.InvariantCulture, out catalogItemId);
    }
}
