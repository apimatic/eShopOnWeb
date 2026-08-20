using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoney
{
    public static string Format(decimal amount, string currency)
    {
        var digits = FractionalDigits(currency);
        return amount.ToString("F" + digits, CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static int FractionalDigits(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "HUF" or "TWD" => 0,
            "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
            _ => 2
        };
    }
}
