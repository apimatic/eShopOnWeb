using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats/parses money for PayPal's string amount fields to the currency's minor-unit precision, so
/// the amount PayPal holds equals the order total to the cent. PayPal rejects amounts with more
/// decimals than the currency allows.
/// </summary>
internal static class CurrencyFormatter
{
    // Currencies with no minor unit (0 decimal places).
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF",
        "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    // Currencies with three decimal places.
    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    public static int Decimals(string currencyCode)
    {
        if (ZeroDecimal.Contains(currencyCode)) return 0;
        if (ThreeDecimal.Contains(currencyCode)) return 3;
        return 2;
    }

    /// <summary>Formats a decimal to PayPal's expected string form for the currency (invariant culture).</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var decimals = Decimals(currencyCode);
        var rounded = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a PayPal money string; returns null when absent or unparseable.</summary>
    public static decimal? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
