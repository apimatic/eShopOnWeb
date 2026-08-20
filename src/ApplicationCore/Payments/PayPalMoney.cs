using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoney
{
    public static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static string ToValue(decimal amount) =>
        Round(amount).ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal FromValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return Round(decimal.Parse(value, CultureInfo.InvariantCulture));
    }
}
