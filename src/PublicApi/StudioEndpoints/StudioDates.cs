using System;
using System.Globalization;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Reads the dates of query strings: a plain date, or a date-time whose date part is meant.</summary>
public static class StudioDates
{
    public static DateOnly? Parse(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // A date-time such as 2026-11-12T00:00:00-08:00: the date as written is the one meant.
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime.DateTime);
        }

        throw StudioException.BadRequest($"'{name}' is not a date. Use yyyy-MM-dd.");
    }
}
