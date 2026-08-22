using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoney
{
    public static string Format(decimal amount, string currency)
    {
        var decimals = IsZeroDecimal(currency) ? 0 : 2;
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static bool AmountsEqual(decimal left, decimal right, string currency)
    {
        var decimals = IsZeroDecimal(currency) ? 0 : 2;
        return Math.Round(left, decimals, MidpointRounding.AwayFromZero)
            == Math.Round(right, decimals, MidpointRounding.AwayFromZero);
    }

    private static bool IsZeroDecimal(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("HUF", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("TWD", StringComparison.OrdinalIgnoreCase);
}
