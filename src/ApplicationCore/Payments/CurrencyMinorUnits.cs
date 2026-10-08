using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Converts decimal amounts to and from integer minor units using ISO 4217 exponents.
/// Conversions are exact: an amount that does not fit the currency's minor unit is rejected, never rounded.
/// </summary>
public static class CurrencyMinorUnits
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    public static int Exponent(string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency)) return 0;
        if (ThreeDecimalCurrencies.Contains(currency)) return 3;
        return 2;
    }

    public static long ToMinor(decimal amount, string currency)
    {
        var scaled = amount * Factor(currency);
        if (scaled != decimal.Truncate(scaled))
        {
            throw new PaymentValidationException(
                $"The amount {amount} cannot be expressed exactly in {currency.ToUpperInvariant()} (at most {Exponent(currency)} decimal places).");
        }
        if (scaled > long.MaxValue || scaled < long.MinValue)
        {
            throw new PaymentValidationException($"The amount {amount} is out of range.");
        }
        return (long)scaled;
    }

    public static decimal FromMinor(long amountMinor, string currency) => amountMinor / Factor(currency);

    private static decimal Factor(string currency) => Exponent(currency) switch
    {
        0 => 1m,
        3 => 1000m,
        _ => 100m
    };
}
