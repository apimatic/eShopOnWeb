using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Converts decimal amounts to and from an integer count of the currency's minor unit, exactly. A value that
/// cannot be represented exactly (for example 10.005 USD) is rejected rather than rounded, so the amount
/// charged always equals the order total to the cent.
/// </summary>
public static class MinorUnits
{
    // ISO 4217 minor-unit exponents that differ from the common 2.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    public static int ExponentOf(string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency)) return 0;
        if (ThreeDecimalCurrencies.Contains(currency)) return 3;
        return 2;
    }

    public static bool TryToMinorUnits(decimal amount, string currency, out long minorUnits)
    {
        minorUnits = 0;
        var scaled = amount * Pow10(ExponentOf(currency));
        if (scaled != decimal.Truncate(scaled)) return false;
        if (scaled > long.MaxValue || scaled < long.MinValue) return false;
        minorUnits = (long)scaled;
        return true;
    }

    public static decimal FromMinorUnits(long minorUnits, string currency) =>
        minorUnits / Pow10(ExponentOf(currency));

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }
}
