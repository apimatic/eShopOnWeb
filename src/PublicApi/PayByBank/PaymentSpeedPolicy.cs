using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

/// <summary>
/// Which payment speeds an order can use right now.
/// Standard ACH is always available. Express is Same Day ACH: Plaid only sends it same-day when the
/// debit (order plus fee) is at most $1,000,000 and is submitted on a business day before the 3:00 PM
/// Eastern cutoff (Plaid advises 15 minutes earlier); otherwise it would ride standard rails while
/// still costing the shopper the fee, so it is not offered.
/// </summary>
public class PaymentSpeedPolicy
{
    public const string Standard = "standard";
    public const string Express = "express";

    /// <summary>Same Day ACH transaction limit.</summary>
    public const decimal ExpressLimit = 1_000_000m;

    /// <summary>Largest single ACH transfer; bigger orders are split across several transfers.</summary>
    public const decimal TransferLimit = 10_000_000m;

    private static readonly TimeSpan ExpressSubmitBefore = new(14, 45, 0);

    private readonly TimeProvider _time;
    private readonly IOptions<PayByBankOptions> _options;

    public PaymentSpeedPolicy(TimeProvider time, IOptions<PayByBankOptions> options)
    {
        _time = time;
        _options = options;
    }

    public decimal ExpressFee => Money.Round(_options.Value.ExpressFee);

    public decimal FeeFor(string speed) => speed == Express ? ExpressFee : 0m;

    public IReadOnlyList<string> OptionsFor(decimal orderTotal)
    {
        var options = new List<string> { Standard };
        if (ExpressIsOffered(orderTotal)) options.Add(Express);
        return options;
    }

    public static bool IsKnown(string? speed) => speed is Standard or Express;

    private bool ExpressIsOffered(decimal orderTotal)
    {
        if (orderTotal + ExpressFee > ExpressLimit) return false;
        var eastern = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), EasternZone());
        return UsBankCalendar.IsBusinessDay(DateOnly.FromDateTime(eastern.DateTime))
               && eastern.TimeOfDay < ExpressSubmitBefore;
    }

    private static TimeZoneInfo EasternZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }
}

/// <summary>Federal Reserve business days (ACH does not settle on weekends or Fed holidays).</summary>
public static class UsBankCalendar
{
    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday && !IsHoliday(date);

    public static bool IsHoliday(DateOnly d)
    {
        var y = d.Year;
        return Observed(new DateOnly(y, 1, 1)) == d
               || NthWeekday(y, 1, DayOfWeek.Monday, 3) == d                 // Martin Luther King Jr. Day
               || NthWeekday(y, 2, DayOfWeek.Monday, 3) == d                 // Washington's Birthday
               || LastWeekday(y, 5, DayOfWeek.Monday) == d                   // Memorial Day
               || (y >= 2021 && Observed(new DateOnly(y, 6, 19)) == d)       // Juneteenth
               || Observed(new DateOnly(y, 7, 4)) == d                       // Independence Day
               || NthWeekday(y, 9, DayOfWeek.Monday, 1) == d                 // Labor Day
               || NthWeekday(y, 10, DayOfWeek.Monday, 2) == d                // Columbus Day
               || Observed(new DateOnly(y, 11, 11)) == d                     // Veterans Day
               || NthWeekday(y, 11, DayOfWeek.Thursday, 4) == d              // Thanksgiving
               || Observed(new DateOnly(y, 12, 25)) == d;                    // Christmas
    }

    /// <summary>Sunday holidays move to Monday; Saturday holidays are not observed on Friday by the Fed.</summary>
    private static DateOnly? Observed(DateOnly holiday) => holiday.DayOfWeek switch
    {
        DayOfWeek.Sunday => holiday.AddDays(1),
        DayOfWeek.Saturday => null,
        _ => holiday
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)day - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek day)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)day + 7) % 7;
        return last.AddDays(-offset);
    }
}
