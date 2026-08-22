using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payment;

public static class PayPalMoney
{
    public static string Format(decimal amount, string currency)
    {
        if (IsZeroDecimalCurrency(currency))
            return decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public static decimal? ParseNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static bool IsZeroDecimalCurrency(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("KRW", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("VND", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("HUF", StringComparison.OrdinalIgnoreCase)
        || currency.Equals("TWD", StringComparison.OrdinalIgnoreCase);
}
