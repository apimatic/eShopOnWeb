using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoney
{
    public static string Format(decimal amount, string currency)
    {
        if (IsZeroDecimal(currency))
        {
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);
        }

        return decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture);
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
        return Format(left, currency) == Format(right, currency);
    }

    private static bool IsZeroDecimal(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("HUF", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("TWD", StringComparison.OrdinalIgnoreCase);
}
