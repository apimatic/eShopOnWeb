using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Formats decimal amounts into the string PayPal expects, and parses them back, using the correct
/// number of minor-unit decimals for the currency. Ensures amounts match to the cent.
/// </summary>
public static class MoneyFormatter
{
    // Currencies with no minor units (PayPal REST currency codes). Everything else uses 2 decimals.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "HUF", "JPY", "TWD"
    };

    public static int Decimals(string currency) =>
        ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;

    public static string Format(decimal amount, string currency)
    {
        var decimals = Decimals(currency);
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
