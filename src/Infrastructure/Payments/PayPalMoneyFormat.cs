using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalMoneyFormat
{
    public static string Format(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// PayPal Transaction Search (RFC 3339 UTC). Fractional seconds and offset
    /// <c>+00:00</c> from round-trip <c>o</c> format are rejected as 400.
    /// </summary>
    public static string FormatSearchInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
