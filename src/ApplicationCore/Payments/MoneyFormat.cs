using System;
using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public static class MoneyFormat
{
    public static string ToPayPalValue(decimal amount)
    {
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static decimal FromPayPalValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var parts = trimmed.Split('/', '-', ' ');
        if (parts.Length == 2)
        {
            var month = parts[0].PadLeft(2, '0');
            var year = parts[1];
            if (year.Length == 2)
            {
                year = "20" + year;
            }

            return $"{year}-{month}";
        }

        throw new ArgumentException("Card expiry must be YYYY-MM or MM/YY.", nameof(expiry));
    }

    public static string DigitsOnly(string value)
    {
        var buffer = new char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (char.IsDigit(c))
            {
                buffer[n++] = c;
            }
        }

        return new string(buffer, 0, n);
    }
}
