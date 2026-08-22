using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoneyFormat
{
    public static string Format(decimal amount, string currency)
    {
        if (string.Equals(currency, "JPY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currency, "HUF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currency, "TWD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currency, "KRW", StringComparison.OrdinalIgnoreCase))
        {
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);
        }

        return amount.ToString("0.00", CultureInfo.InvariantCulture);
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
