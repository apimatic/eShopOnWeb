using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>
/// Maps between eShop catalog item ids and the seller SKUs used on Walmart Marketplace.
/// A catalog item is offered on Walmart under the seller SKU <c>ESHOP-{catalogItemId}</c>.
/// </summary>
public static class WalmartSku
{
    public const string Prefix = "ESHOP-";

    public static string ForCatalogItemId(int catalogItemId) => $"{Prefix}{catalogItemId}";

    /// <summary>
    /// Attempts to recover the eShop catalog item id from a seller SKU. Returns false for any
    /// SKU that does not follow the <c>ESHOP-{positive integer}</c> shape (e.g. a SKU that was
    /// created on Walmart outside eShop).
    /// </summary>
    public static bool TryGetCatalogItemId(string? sku, out int catalogItemId)
    {
        catalogItemId = 0;
        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        if (!sku.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var idPart = sku.Substring(Prefix.Length);
        return int.TryParse(idPart, NumberStyles.None, CultureInfo.InvariantCulture, out catalogItemId)
            && catalogItemId > 0;
    }
}
