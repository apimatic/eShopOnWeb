using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class PayPalMoneyFormat
{
    public static string ToValue(decimal amount, string currency)
    {
        var digits = FractionDigits(currency);
        var rounded = decimal.Round(amount, digits, MidpointRounding.AwayFromZero);
        return rounded.ToString("F" + digits, CultureInfo.InvariantCulture);
    }

    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static int FractionDigits(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW"
                or "PYG" or "RWF" or "UGX" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
            "BHD" or "JOD" or "KWD" or "OMR" or "TND" => 3,
            _ => 2
        };
    }
}
