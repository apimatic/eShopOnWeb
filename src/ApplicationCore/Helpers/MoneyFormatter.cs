using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Helpers;

public static class MoneyFormatter
{
    public static string ToPayPalAmount(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal? ParsePayPalAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}