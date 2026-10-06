using System;
using System.Collections.Generic;
using Square.Models;
using Square.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>Converts eShop decimal prices to Square <see cref="Money"/> (amounts in the currency's smallest unit).</summary>
public static class SquareMoney
{
    // ISO 4217 currencies without a minor unit; everything else in Square's list uses two decimals
    // except the three-decimal currencies below.
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.Ordinal)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF",
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.Ordinal)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND",
    };

    public static long ToMinorUnits(decimal amount, Currency currency)
    {
        var factor = Factor(currency);
        return decimal.ToInt64(decimal.Round(amount * factor, 0, MidpointRounding.AwayFromZero));
    }

    public static decimal FromMinorUnits(long amount, Currency currency) => amount / Factor(currency);

    public static Money From(decimal amount, Currency currency) => new()
    {
        Amount = ToMinorUnits(amount, currency),
        Currency = currency,
    };

    public static bool Matches(Money? money, decimal amount, Currency currency) =>
        money?.Amount is { } squareAmount
        && money.Currency is { } squareCurrency
        && squareCurrency.Value == currency.Value
        && squareAmount == ToMinorUnits(amount, currency);

    private static decimal Factor(Currency currency) =>
        ZeroDecimal.Contains(currency.Value) ? 1m : ThreeDecimal.Contains(currency.Value) ? 1000m : 100m;
}
