using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class MoneyFormat
{
    public static decimal ToCents(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static string ToPayPalValue(decimal amount) =>
        ToCents(amount).ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? ToCents(parsed)
            : 0m;
    }
}
