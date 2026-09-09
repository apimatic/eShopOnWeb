using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats decimal amounts to the string PayPal expects for a currency, and parses them back. PayPal
/// requires the correct number of decimal places per currency; getting this right is what keeps a
/// held/captured amount equal to the order total "to the cent".
/// </summary>
public static class CurrencyFormatter
{
    // Currencies PayPal does not accept decimal digits for.
    private static readonly HashSet<string> ZeroDecimalCurrencies =
        new(StringComparer.OrdinalIgnoreCase) { "HUF", "JPY", "TWD" };

    public static int DecimalPlaces(string currencyCode) =>
        ZeroDecimalCurrencies.Contains(currencyCode) ? 0 : 2;

    /// <summary>Format a decimal to a PayPal amount string in the invariant culture.</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var dp = DecimalPlaces(currencyCode);
        var rounded = Math.Round(amount, dp, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + dp.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Parse a PayPal amount string to a decimal; returns null on a missing/invalid value.</summary>
    public static decimal? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
