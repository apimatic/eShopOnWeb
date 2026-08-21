using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoneyFormatter
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    public static string Format(decimal amount, string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);
        }

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static decimal Round(decimal amount, string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return decimal.Truncate(amount);
        }

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
