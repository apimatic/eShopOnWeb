using System;
using System.Globalization;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Payment;

public static class PayPalMoney
{
    private static readonly string[] ZeroDecimalCurrencies = { "JPY", "KRW", "HUF", "TWD" };

    public static string Format(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        if (ZeroDecimalCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
            return Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        return Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static decimal Round(decimal amount, string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
            return Math.Round(amount, 0, MidpointRounding.AwayFromZero);

        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
