using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Formats decimal amounts to the string PayPal expects, using the currency's number of minor units,
/// and parses PayPal's string amounts back to decimal. Keeps the "amount held equals order total to
/// the cent" guarantee independent of locale.
/// </summary>
public static class MoneyFormatter
{
    // Currencies PayPal does not accept decimal places for (0 minor units).
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "HUF", "JPY", "TWD"
    };

    // Currencies with 3 minor units.
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "KWD", "OMR", "TND"
    };

    public static int MinorUnits(string currencyCode)
    {
        if (ZeroDecimalCurrencies.Contains(currencyCode)) return 0;
        if (ThreeDecimalCurrencies.Contains(currencyCode)) return 3;
        return 2;
    }

    /// <summary>Renders an amount as a fixed-point string in the currency's minor units, e.g. "123.45".</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var digits = MinorUnits(currencyCode);
        var rounded = Math.Round(amount, digits, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + digits, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static decimal? TryParse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
