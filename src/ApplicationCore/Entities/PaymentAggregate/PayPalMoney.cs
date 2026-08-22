using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PayPalMoney
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    public static string Format(decimal amount, string currency)
    {
        var rounded = Round(amount, currency);
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        }

        return rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal Round(decimal amount, string currency)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;
        return decimal.Round(amount, decimals, MidpointRounding.AwayFromZero);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}
