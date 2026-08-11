using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats and parses PayPal money values. PayPal sends amounts as strings with the currency's exact number
/// of decimal places; getting this right is what keeps the held/captured amount equal to the order total to the cent.
/// </summary>
internal static class PayPalMoney
{
    // Currencies with zero or three decimal places; everything else uses two.
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "HUF", "TWD", "ISK", "UGX", "RWF", "DJF", "GNF", "KMF", "PYG", "VUV", "XAF", "XOF", "XPF", "BIF"
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "KWD", "OMR", "TND"
    };

    public static int DecimalPlaces(string currencyCode)
    {
        if (ZeroDecimal.Contains(currencyCode)) return 0;
        if (ThreeDecimal.Contains(currencyCode)) return 3;
        return 2;
    }

    public static string Format(decimal amount, string currencyCode)
    {
        var places = DecimalPlaces(currencyCode);
        var rounded = Math.Round(amount, places, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + places, CultureInfo.InvariantCulture);
    }

    public static decimal? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
