using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class CardExpiryNormalizer
{
    public static string Normalize(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new ArgumentException("Card expiry is required.", nameof(expiry));
        }

        var trimmed = expiry.Trim();
        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}$"))
        {
            return trimmed;
        }

        if (Regex.IsMatch(trimmed, @"^\d{2}/\d{4}$"))
        {
            var parts = trimmed.Split('/');
            return $"{parts[1]}-{parts[0]}";
        }

        if (Regex.IsMatch(trimmed, @"^\d{2}/\d{2}$"))
        {
            var parts = trimmed.Split('/');
            var year = 2000 + int.Parse(parts[1], CultureInfo.InvariantCulture);
            return $"{year:D4}-{parts[0]}";
        }

        throw new ArgumentException("Card expiry must be YYYY-MM or MM/YYYY.", nameof(expiry));
    }
}
