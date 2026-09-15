using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats and parses PayPal money values. PayPal transports amounts as strings and requires the
/// number of decimal places that matches the currency, so the held/captured amount equals the order
/// total to the cent.
/// </summary>
public static class PayPalMoney
{
    // Currencies PayPal does not accept decimals for.
    private static readonly HashSet<string> ZeroDecimalCurrencies =
        new(StringComparer.OrdinalIgnoreCase) { "HUF", "JPY", "TWD" };

    public static int DecimalsFor(string currency) =>
        ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;

    public static string Format(decimal amount, string currency)
    {
        var decimals = DecimalsFor(currency);
        // Round half-away-from-zero to the currency's scale, then render with a fixed number of places.
        var rounded = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
