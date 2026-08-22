using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class MoneyFormatter
{
    public static string ToPayPalValue(decimal amount, string currency)
    {
        var code = currency.ToUpperInvariant();
        if (code is "JPY" or "HUF" or "TWD" or "KRW")
        {
            return decimal.Round(amount, 0, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);
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

    public static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
