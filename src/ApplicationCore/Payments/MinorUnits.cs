using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Converts decimal prices to the integer minor units payment providers charge in (cents for USD).
/// Conversion is exact or it fails — an amount is never rounded, so what is charged always equals the
/// order total to the minor unit.
/// </summary>
public static class MinorUnits
{
    // ISO 4217 currencies whose minor unit is not 1/100.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    public static int Exponent(string currency) =>
        ZeroDecimalCurrencies.Contains(currency) ? 0 : ThreeDecimalCurrencies.Contains(currency) ? 3 : 2;

    public static bool TryFromDecimal(decimal amount, string currency, out long minorUnits)
    {
        minorUnits = 0;
        var scaled = amount * Pow10(Exponent(currency));
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue || scaled < long.MinValue)
        {
            return false;
        }

        minorUnits = (long)scaled;
        return true;
    }

    public static decimal ToDecimal(long minorUnits, string currency) => minorUnits / Pow10(Exponent(currency));

    private static decimal Pow10(int exponent) => exponent switch { 0 => 1m, 3 => 1000m, _ => 100m };
}
