using System;
using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

/// <summary>
/// Parses the date-time strings that Maxio returns. The spec types these as date-time but does
/// not pin a timezone shape (examples show both offsets and Z), so parsing must be tolerant and
/// never throw for the client's sake.
/// </summary>
public static class MaxioDateTime
{
    public static DateTimeOffset? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Maxio emits offsets like "2026-09-08T22:56:55+05:00" or "Z".
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            return withOffset;
        }

        // Tolerate space-separated offsets (e.g. "2016-11-08 16:22:26 -0500").
        if (DateTimeOffset.TryParse(value.Replace(' ', 'T'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var normalized))
        {
            return normalized;
        }

        return null;
    }
}
