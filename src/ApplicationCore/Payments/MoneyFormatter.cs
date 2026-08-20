using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class MoneyFormatter
{
    public static decimal ToMajorUnits(decimal amount, string currency)
    {
        var decimals = DecimalPlaces(currency);
        return decimal.Round(amount, decimals, System.MidpointRounding.AwayFromZero);
    }

    public static string ToPayPalValue(decimal amount, string currency)
    {
        var rounded = ToMajorUnits(amount, currency);
        var decimals = DecimalPlaces(currency);
        return rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static int DecimalPlaces(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "JPY" or "KRW" or "HUF" or "TWD" => 0,
            _ => 2
        };
    }
}
