using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats decimal amounts to the string PayPal expects (its <c>Money.value</c> is a string, and it
/// does no rounding), and parses them back. The value is rendered to the currency's supported number
/// of decimal places so the amount held/captured matches the order total to the cent.
/// </summary>
public static class MoneyFormatter
{
    // Currencies that do not use decimal places, and those that use three (ISO-4217).
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "ISK", "HUF", "TWD"
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "JOD", "KWD", "OMR", "TND"
    };

    public static int DecimalDigits(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode)) return 2;
        if (ZeroDecimal.Contains(currencyCode)) return 0;
        if (ThreeDecimal.Contains(currencyCode)) return 3;
        return 2;
    }

    public static string Format(decimal amount, string currencyCode)
    {
        var digits = DecimalDigits(currencyCode);
        var rounded = Math.Round(amount, digits, MidpointRounding.ToEven);
        return rounded.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (decimal?)null;
    }
}
