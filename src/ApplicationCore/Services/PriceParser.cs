using System.Globalization;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Parses a price as displayed on a supplier's listing page into a catalog price.
/// A listing may show a non-numeric price (e.g. "Contact for pricing"); those return false so
/// the product is counted as found but not imported.
/// </summary>
public static class PriceParser
{
    public static bool TryParse(string? priceText, out decimal price)
    {
        price = 0m;
        if (string.IsNullOrWhiteSpace(priceText))
        {
            return false;
        }

        // Grab the first number-like token, allowing thousands and decimal separators.
        var match = Regex.Match(priceText, @"[0-9][0-9.,]*");
        if (!match.Success)
        {
            return false;
        }

        // Treat ',' as a thousands separator and '.' as the decimal point (matches the currency
        // formats these listings use). Strip commas, then parse invariantly.
        var token = match.Value.Replace(",", string.Empty);

        if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
        {
            price = 0m;
            return false;
        }

        return price > 0m;
    }
}
