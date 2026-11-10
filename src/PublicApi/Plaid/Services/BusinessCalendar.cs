using System;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

/// <summary>US banking days (weekends and Federal Reserve holidays are closed).</summary>
public static class BusinessCalendar
{
    private static readonly TimeZoneInfo Eastern = FindEastern();

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("ET", TimeSpan.FromHours(-5), "ET", "ET");
    }

    public static DateTime EasternNow(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Eastern).DateTime;

    public static bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday && !IsHoliday(date.Date);

    public static DateTime NextBusinessDay(DateTime date)
    {
        var d = date.Date.AddDays(1);
        while (!IsBusinessDay(d)) d = d.AddDays(1);
        return d;
    }

    public static DateTime AddBusinessDays(DateTime date, int days)
    {
        var d = date.Date;
        for (var i = 0; i < days; i++) d = NextBusinessDay(d);
        return d;
    }

    /// <summary>The business day on which a submission made now is processed.</summary>
    public static DateTime ProcessingDay(DateTimeOffset now, TimeSpan cutoff)
    {
        var local = EasternNow(now);
        return IsBusinessDay(local) && local.TimeOfDay < cutoff ? local.Date : NextBusinessDay(local);
    }

    private static bool IsHoliday(DateTime d)
    {
        // Observed on the nearest weekday for fixed-date holidays; a Saturday holiday is not observed by the Fed.
        bool Fixed(int month, int day)
        {
            var h = new DateTime(d.Year, month, day);
            if (h.DayOfWeek == DayOfWeek.Sunday) h = h.AddDays(1);
            return h == d;
        }

        bool Nth(int month, DayOfWeek dow, int n)
        {
            var first = new DateTime(d.Year, month, 1);
            var offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + 7 * (n - 1)) == d;
        }

        bool Last(int month, DayOfWeek dow)
        {
            var last = new DateTime(d.Year, month, DateTime.DaysInMonth(d.Year, month));
            var offset = ((int)last.DayOfWeek - (int)dow + 7) % 7;
            return last.AddDays(-offset) == d;
        }

        return Fixed(1, 1) || Nth(1, DayOfWeek.Monday, 3) || Nth(2, DayOfWeek.Monday, 3) || Last(5, DayOfWeek.Monday)
            || Fixed(6, 19) || Fixed(7, 4) || Nth(9, DayOfWeek.Monday, 1) || Nth(10, DayOfWeek.Monday, 2)
            || Fixed(11, 11) || Nth(11, DayOfWeek.Thursday, 4) || Fixed(12, 25);
    }
}
