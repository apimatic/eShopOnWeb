using System;
using System.Collections.Generic;
using Square.Models;
using Square.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Converts eShop decimal prices to Square <see cref="Money"/>, whose amount is in the currency's smallest
/// denomination (cents for USD).
/// </summary>
public static class SquareMoney
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.Ordinal)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.Ordinal)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND",
    };

    public static int MinorUnits(string currencyCode) =>
        ZeroDecimalCurrencies.Contains(currencyCode) ? 0 : ThreeDecimalCurrencies.Contains(currencyCode) ? 3 : 2;

    /// <summary>Converts a price to minor units; refuses prices that do not fit the currency exactly.</summary>
    public static long ToMinorUnits(decimal price, string currencyCode)
    {
        var scaled = price * Pow10(MinorUnits(currencyCode));
        if (scaled != decimal.Truncate(scaled))
            throw new SquareIntegrationException(SquareFailureKind.Rejected,
                $"Price {price} cannot be expressed exactly in {currencyCode}.");
        return decimal.ToInt64(scaled);
    }

    public static decimal FromMinorUnits(long amount, string currencyCode) =>
        amount / Pow10(MinorUnits(currencyCode));

    public static Money ToMoney(decimal price, Currency currency) => new()
    {
        Amount = ToMinorUnits(price, currency.Value),
        Currency = currency,
    };

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }
}
