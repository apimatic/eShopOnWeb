using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalMoneyFormatter
{
    public static string Format(decimal amount, string currency)
    {
        var decimals = ZeroDecimal(currency) ? 0 : 2;
        var rounded = decimal.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return decimals == 0
            ? decimal.Truncate(rounded).ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static bool ZeroDecimal(string currency)
    {
        return currency.ToUpperInvariant() is "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "HUF" or "TWD";
    }
}
