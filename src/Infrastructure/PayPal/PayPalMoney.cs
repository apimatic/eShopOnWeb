using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats decimal amounts into the string form PayPal's <c>Money.value</c> expects, at the currency's
/// minor-unit precision, so a held/captured amount equals the order total to the cent. This is ordinary
/// ISO-4217 money formatting (not a PayPal API detail): most currencies use 2 decimal places; a few use 0.
/// </summary>
public static class PayPalMoney
{
    // ISO-4217 currencies with zero minor units (no fractional part). Not exhaustive of every world
    // currency, but covers the common zero-decimal codes so a non-USD deployment formats correctly.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "ISK", "HUF", "TWD", "UGX", "XAF", "XOF", "XPF", "RWF", "BIF", "DJF", "GNF", "KMF", "PYG", "VUV"
    };

    public static int DecimalPlaces(string currencyCode) =>
        ZeroDecimalCurrencies.Contains(currencyCode) ? 0 : 2;

    /// <summary>Formats <paramref name="amount"/> at the currency's precision, invariant culture (e.g. "12.30").</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var places = DecimalPlaces(currencyCode);
        var rounded = Math.Round(amount, places, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + places, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a PayPal money value string back to decimal, invariant culture. Returns null if unparseable.</summary>
    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : (decimal?)null;
    }
}
