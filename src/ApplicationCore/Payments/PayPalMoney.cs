using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoney
{
    public static string Format(decimal amount, string currency)
    {
        var decimals = MinorUnits(currency);
        return amount.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static decimal Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    public static decimal Round(decimal amount, string currency)
    {
        return decimal.Round(amount, MinorUnits(currency), MidpointRounding.AwayFromZero);
    }

    public static int MinorUnits(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "JPY" or "KRW" or "VND" or "CLP" => 0,
            "BHD" or "JOD" or "KWD" or "OMR" or "TND" => 3,
            _ => 2
        };
    }
}
