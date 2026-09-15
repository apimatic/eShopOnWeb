using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class MoneyFormat
{
    public static string ToPayPalValue(decimal amount, string currencyCode)
    {
        var decimals = DecimalPlaces(currencyCode);
        return decimal.Round(amount, decimals).ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static int DecimalPlaces(string? currencyCode)
    {
        return currencyCode?.ToUpperInvariant() switch
        {
            "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "HUF" or "TWD" => 0,
            "BHD" or "JOD" or "KWD" or "OMR" or "TND" => 3,
            _ => 2
        };
    }
}
