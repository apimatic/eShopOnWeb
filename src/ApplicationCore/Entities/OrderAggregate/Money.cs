using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class Money
{
    public static decimal ToCents(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static string ToPayPalValue(decimal amount) =>
        ToCents(amount).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return ToCents(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
    }
}
