using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoneyFormat
{
    public static string ToApiValue(decimal amount, string currency)
    {
        if (IsZeroDecimal(currency))
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static bool AmountsEqual(decimal left, decimal right, string currency)
    {
        var scale = IsZeroDecimal(currency) ? 0 : 2;
        return decimal.Round(left, scale, MidpointRounding.AwayFromZero)
            == decimal.Round(right, scale, MidpointRounding.AwayFromZero);
    }

    private static bool IsZeroDecimal(string currency) =>
        currency is "JPY" or "KRW" or "VND" or "HUF" or "TWD";
}
