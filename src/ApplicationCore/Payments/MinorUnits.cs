using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Converts between decimal amounts and the integer minor units payment providers charge in
/// (ISO 4217 exponents). Never rounds: an amount that cannot be represented exactly is refused.
/// </summary>
public static class MinorUnits
{
    private static readonly Dictionary<string, int> NonStandardExponents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["UYI"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0,
        ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
    };

    public static int Exponent(string currency) =>
        NonStandardExponents.TryGetValue(currency, out var exponent) ? exponent : 2;

    public static bool TryToMinor(decimal amount, string currency, out long minor)
    {
        var scaled = amount * Factor(currency);
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue || scaled < long.MinValue)
        {
            minor = 0;
            return false;
        }

        minor = (long)scaled;
        return true;
    }

    public static decimal FromMinor(long minor, string currency) => minor / Factor(currency);

    private static decimal Factor(string currency)
    {
        var factor = 1m;
        for (var i = 0; i < Exponent(currency); i++) factor *= 10m;
        return factor;
    }
}
