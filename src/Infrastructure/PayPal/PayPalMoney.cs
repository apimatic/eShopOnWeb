using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats and parses PayPal money <c>value</c> strings, which must be serialised as a string with the number of
/// decimal places the ISO-4217 currency uses (2 for most, 0 for e.g. JPY, 3 for e.g. BHD).
/// </summary>
internal static class PayPalMoney
{
    // Currencies whose minor unit differs from the common 2 decimal places.
    private static readonly Dictionary<string, int> Exponents = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // Zero-decimal currencies
        ["JPY"] = 0, ["KRW"] = 0, ["VND"] = 0, ["CLP"] = 0, ["HUF"] = 0, ["TWD"] = 0,
        ["ISK"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0, ["RWF"] = 0, ["UGX"] = 0,
        ["DJF"] = 0, ["GNF"] = 0, ["KMF"] = 0, ["PYG"] = 0, ["BIF"] = 0, ["VUV"] = 0,
        // Three-decimal currencies
        ["BHD"] = 3, ["KWD"] = 3, ["OMR"] = 3, ["TND"] = 3, ["JOD"] = 3, ["LYD"] = 3, ["IQD"] = 3
    };

    public static int Decimals(string currencyCode) =>
        currencyCode is not null && Exponents.TryGetValue(currencyCode, out var d) ? d : 2;

    public static string Format(decimal amount, string currencyCode)
    {
        var decimals = Decimals(currencyCode);
        return amount.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal? ParseOrNull(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    public static decimal Parse(string? value) => ParseOrNull(value) ?? 0m;
}
