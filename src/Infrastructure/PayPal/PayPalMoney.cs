using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Formats and parses monetary amounts the way PayPal's APIs expect: a string value whose number of decimal
/// places depends on the currency (2 for most, 0 for currencies like JPY, 3 for currencies like KWD).
/// </summary>
public static class PayPalMoneyFormatter
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "HUF", "TWD", "CLP", "ISK"
    };

    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "KWD", "OMR", "TND", "IQD", "JOD", "LYD"
    };

    public static int Decimals(string currencyCode)
    {
        if (ZeroDecimalCurrencies.Contains(currencyCode)) return 0;
        if (ThreeDecimalCurrencies.Contains(currencyCode)) return 3;
        return 2;
    }

    /// <summary>Formats a decimal to PayPal's string form, rounding to the currency's scale (banker's-safe away-from-zero).</summary>
    public static string Format(decimal value, string currencyCode)
    {
        var decimals = Decimals(currencyCode);
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static string Format(PayPalMoney money) => Format(money.Value, money.CurrencyCode);

    public static decimal Parse(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
