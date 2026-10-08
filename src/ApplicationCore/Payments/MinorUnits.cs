using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Converts decimal amounts to the integer minor units a card processor charges in, exactly or not at all.
/// </summary>
public static class MinorUnits
{
    // ISO-4217 currencies whose minor unit is not 1/100. Every other well-formed code uses two decimals.
    private static readonly Dictionary<string, int> NonDefaultExponents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0,
        ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["UYI"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
    };

    public static bool IsWellFormedCurrency(string? currency) =>
        currency is { Length: 3 } && currency.AsSpan().IndexOfAnyExceptInRange('A', 'Z') < 0;

    public static int ExponentOf(string currency)
    {
        if (!IsWellFormedCurrency(currency))
            throw new ArgumentException("Currency must be a three-letter upper-case ISO-4217 code.", nameof(currency));
        return NonDefaultExponents.TryGetValue(currency, out var exponent) ? exponent : 2;
    }

    /// <summary>Returns false when <paramref name="amount"/> has more precision than the currency's minor unit.</summary>
    public static bool TryConvert(decimal amount, string currency, out long minorUnits)
    {
        minorUnits = 0;
        if (!IsWellFormedCurrency(currency)) return false;

        var scaled = amount * Pow10(ExponentOf(currency));
        if (scaled != decimal.Truncate(scaled) || scaled > long.MaxValue || scaled < long.MinValue) return false;

        minorUnits = (long)scaled;
        return true;
    }

    public static decimal ToDecimal(long minorUnits, string currency) => minorUnits / Pow10(ExponentOf(currency));

    private static decimal Pow10(int exponent) => exponent switch { 0 => 1m, 2 => 100m, 3 => 1000m, _ => throw new ArgumentOutOfRangeException(nameof(exponent)) };
}
