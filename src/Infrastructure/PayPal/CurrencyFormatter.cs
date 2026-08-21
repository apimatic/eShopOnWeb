using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats and parses PayPal money values. PayPal carries amounts as strings in the currency's
/// minor units ("to the cent"), so this rounds and formats to the right number of decimal places
/// for the configured currency.
/// </summary>
public static class CurrencyFormatter
{
    // ISO-4217 currencies with no minor unit (whole-number amounts).
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "XOF", "XAF", "XPF", "BIF", "DJF",
        "GNF", "KMF", "MGA", "PYG", "RWF", "UGX", "VUV"
    };

    // Currencies with three minor-unit digits.
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "KWD", "OMR", "TND", "JOD", "IQD", "LYD"
    };

    public static int DecimalDigits(string currencyCode)
    {
        if (ZeroDecimalCurrencies.Contains(currencyCode)) return 0;
        if (ThreeDecimalCurrencies.Contains(currencyCode)) return 3;
        return 2;
    }

    /// <summary>Formats an amount as a PayPal money string in the currency's minor units.</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var digits = DecimalDigits(currencyCode);
        var rounded = Math.Round(amount, digits, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a PayPal money string back to a decimal.</summary>
    public static decimal Parse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0m
            : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
