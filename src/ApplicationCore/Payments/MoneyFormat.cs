using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class MoneyFormat
{
    public static string ToPayPalValue(decimal amount, string currencyCode)
    {
        var decimals = DecimalPlaces(currencyCode);
        var rounded = decimal.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return rounded.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
    }

    public static decimal Round(decimal amount, string currencyCode)
    {
        return decimal.Round(amount, DecimalPlaces(currencyCode), MidpointRounding.AwayFromZero);
    }

    public static int DecimalPlaces(string currencyCode)
    {
        return currencyCode.ToUpperInvariant() switch
        {
            "JPY" or "KRW" or "HUF" or "TWD" => 0,
            "TND" or "BHD" or "JOD" or "KWD" or "OMR" => 3,
            _ => 2
        };
    }
}
