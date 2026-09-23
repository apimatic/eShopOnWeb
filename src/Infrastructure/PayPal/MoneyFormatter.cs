using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats decimal amounts to the string shape PayPal expects for a currency, respecting the currency's
/// number of minor units. PayPal amounts are strings (e.g. "12.34"); getting the decimal count wrong is
/// rejected by PayPal, so this uses each currency's documented precision.
/// </summary>
public static class MoneyFormatter
{
    // Zero-decimal currencies per the PayPal REST currency-codes reference.
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "HUF", "JPY", "TWD"
    };

    // Currencies that do not support decimals in PayPal even though ISO allows them.
    private static readonly HashSet<string> NoDecimalSupport = new(StringComparer.OrdinalIgnoreCase)
    {
        "HUF", "TWD"
    };

    public static int DecimalPlaces(string currencyCode) =>
        ZeroDecimal.Contains(currencyCode) || NoDecimalSupport.Contains(currencyCode) ? 0 : 2;

    /// <summary>Formats an amount to PayPal's string form for the currency, using invariant culture.</summary>
    public static string Format(decimal amount, string currencyCode)
    {
        var places = DecimalPlaces(currencyCode);
        var rounded = Math.Round(amount, places, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + places, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a PayPal amount string back to a decimal; null/blank/unparsable yields null.</summary>
    public static decimal? Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
}
