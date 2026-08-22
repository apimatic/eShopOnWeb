using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public static class MoneyFormatter
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    public static int DecimalPlaces(string currency) =>
        ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;

    public static decimal Round(decimal amount, string currency) =>
        Math.Round(amount, DecimalPlaces(currency), MidpointRounding.AwayFromZero);

    public static string ToPayPalValue(decimal amount, string currency) =>
        Round(amount, currency).ToString("F" + DecimalPlaces(currency), CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static bool AmountsEqual(decimal left, decimal right, string currency) =>
        Round(left, currency) == Round(right, currency);
}
