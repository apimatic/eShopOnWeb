using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class MoneyFormatter
{
    public static string ToPayPalValue(decimal amount, string currency)
    {
        var decimals = IsZeroDecimal(currency) ? 0 : 2;
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString(decimals == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
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
        var decimals = IsZeroDecimal(currency) ? 0 : 2;
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
    }

    private static bool IsZeroDecimal(string currency) =>
        string.Equals(currency, "JPY", StringComparison.OrdinalIgnoreCase)
        || string.Equals(currency, "HUF", StringComparison.OrdinalIgnoreCase)
        || string.Equals(currency, "TWD", StringComparison.OrdinalIgnoreCase);
}
