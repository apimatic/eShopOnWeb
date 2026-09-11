using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats and parses monetary values the way PayPal's <c>money</c> schema requires: a string
/// <c>value</c> with the number of decimal places defined for the currency. This is what makes the
/// amount PayPal holds equal the order total to the cent.
/// </summary>
public static class MoneyFormatter
{
    // Currencies PayPal does not support decimals for. Everything else defaults to 2 decimals,
    // which covers USD/EUR/GBP and the vast majority of ISO-4217 currencies.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "HUF", "JPY", "TWD"
    };

    // Currencies PayPal requires exactly 3 decimals for.
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "KWD", "OMR", "TND"
    };

    public static int DecimalPlaces(string currencyCode)
    {
        if (ZeroDecimalCurrencies.Contains(currencyCode)) return 0;
        if (ThreeDecimalCurrencies.Contains(currencyCode)) return 3;
        return 2;
    }

    /// <summary>Format a decimal to the exact string PayPal expects for the currency.</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var decimals = DecimalPlaces(currencyCode);
        var rounded = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>Parse a PayPal money string back into a decimal.</summary>
    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0m;
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }
}
