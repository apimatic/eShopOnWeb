using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioDate
{
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
