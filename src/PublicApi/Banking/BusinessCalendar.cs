using System;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>US banking days (ACH settles on weekdays that are not Federal Reserve holidays).</summary>
public static class BusinessCalendar
{
    private static readonly TimeZoneInfo Eastern = FindEastern();

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    public static DateTime EasternNow(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Eastern).DateTime;

    public static bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date.Date);

    public static DateTime NextBusinessDay(DateTime date)
    {
        var d = date.Date.AddDays(1);
        while (!IsBusinessDay(d))
        {
            d = d.AddDays(1);
        }

        return d;
    }

    public static DateTime OnOrAfterBusinessDay(DateTime date)
    {
        var d = date.Date;
        while (!IsBusinessDay(d))
        {
            d = d.AddDays(1);
        }

        return d;
    }

    /// <summary>Federal Reserve holidays. One on a Sunday is observed on the Monday; one on a Saturday is not moved.</summary>
    private static bool IsHoliday(DateTime d)
    {
        var y = d.Year;
        // Dates observed on d: a holiday on Sunday is observed on Monday; a Jan 1 of next year on Saturday is not moved.
        return Observed(new DateTime(y, 1, 1), d) ||
               d == NthWeekday(y, 1, DayOfWeek.Monday, 3) ||
               d == NthWeekday(y, 2, DayOfWeek.Monday, 3) ||
               d == LastWeekday(y, 5, DayOfWeek.Monday) ||
               Observed(new DateTime(y, 6, 19), d) ||
               Observed(new DateTime(y, 7, 4), d) ||
               d == NthWeekday(y, 9, DayOfWeek.Monday, 1) ||
               d == NthWeekday(y, 10, DayOfWeek.Monday, 2) ||
               Observed(new DateTime(y, 11, 11), d) ||
               d == NthWeekday(y, 11, DayOfWeek.Thursday, 4) ||
               Observed(new DateTime(y, 12, 25), d);
    }

    private static bool Observed(DateTime holiday, DateTime d) =>
        holiday.DayOfWeek == DayOfWeek.Sunday ? d == holiday.AddDays(1) : d == holiday;

    private static DateTime NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var d = new DateTime(year, month, 1);
        while (d.DayOfWeek != day)
        {
            d = d.AddDays(1);
        }

        return d.AddDays(7 * (n - 1));
    }

    private static DateTime LastWeekday(int year, int month, DayOfWeek day)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        while (d.DayOfWeek != day)
        {
            d = d.AddDays(-1);
        }

        return d;
    }
}
