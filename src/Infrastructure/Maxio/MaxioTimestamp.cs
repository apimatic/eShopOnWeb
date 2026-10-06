using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Parses the timestamp formats used by the Maxio Billing API, e.g.
/// ISO-8601 ("2025-02-13T18:46:49Z") and the legacy
/// "2025-02-13 18:46:49 -0500" format (offset without a colon).
/// </summary>
public static class MaxioTimestamp
{
    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = NormalizeOffset(value.Trim());

        if (DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Converts a trailing "-HHMM"/"+HHMM" offset into "-HH:MM"/"+HH:MM" so
    /// DateTimeOffset can parse the legacy Chargify format.
    /// </summary>
    private static string NormalizeOffset(string value)
    {
        if (value.Length < 5)
        {
            return value;
        }

        var sign = value[^5];
        if (sign != '-' && sign != '+')
        {
            return value;
        }

        var fourDigits = value.Substring(value.Length - 4);
        if (!char.IsDigit(fourDigits[0]) || !char.IsDigit(fourDigits[1]) || !char.IsDigit(fourDigits[2]) || !char.IsDigit(fourDigits[3]))
        {
            return value;
        }

        if (value.Contains(' ', StringComparison.Ordinal) || value.Contains('T', StringComparison.Ordinal))
        {
            return value.Insert(value.Length - 2, ":");
        }

        return value;
    }
}