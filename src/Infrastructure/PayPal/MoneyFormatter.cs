using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats decimal amounts into PayPal's amount <c>value</c> string (and back), honoring each
/// currency's decimal precision so the held/captured amount matches the order total to the cent.
/// </summary>
public static class MoneyFormatter
{
    // Currencies PayPal treats as zero- or three-decimal; everything else is two decimals.
    private static readonly HashSet<string> ZeroDecimal = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };
    private static readonly HashSet<string> ThreeDecimal = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "KWD", "OMR", "TND"
    };

    public static int DecimalDigits(string currency)
    {
        if (ZeroDecimal.Contains(currency)) return 0;
        if (ThreeDecimal.Contains(currency)) return 3;
        return 2;
    }

    /// <summary>Formats an amount as PayPal expects, e.g. 12.5m + "USD" => "12.50".</summary>
    public static string Format(decimal amount, string currency)
    {
        var digits = DecimalDigits(currency);
        var rounded = System.Math.Round(amount, digits, System.MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a PayPal amount value string; returns null if it is missing/unparseable.</summary>
    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }
}
