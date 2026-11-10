using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.eShopWeb.PublicApi.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public class BankPaymentOptions
{
    public const string SectionName = "BankPayment";

    /// <summary>Fee the shopper pays for express (same-day) payments.</summary>
    public decimal ExpressFee { get; set; } = 5.00m;
    /// <summary>Largest single ACH transfer; bigger orders are charged as several transfers.</summary>
    public decimal AchLimit { get; set; } = 10_000_000m;
    /// <summary>Largest single same-day ACH transfer; express is only offered up to this.</summary>
    public decimal SameDayAchLimit { get; set; } = 1_000_000m;
    /// <summary>Business days a standard payment takes to arrive.</summary>
    public int StandardBusinessDays { get; set; } = 3;
}

public static class Json
{
    public static JsonArray Arr(params string[] values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(v);
        return a;
    }

    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static decimal Dec(JsonNode? node)
    {
        if (node is not JsonValue v) return 0m;
        if (v.TryGetValue<decimal>(out var d)) return d;
        if (v.TryGetValue<double>(out var dbl)) return (decimal)dbl;
        return 0m;
    }
}

public static class CurrentUser
{
    /// <summary>The caller's identity, taken from the JWT.</summary>
    public static string Name(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name")
            ?? user.FindFirstValue("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        }
        return name;
    }
}

public class TokenProtector
{
    private readonly IDataProtector _protector;

    public TokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("eShopOnWeb.Plaid.AccessToken");
    }

    public string Protect(string value) => _protector.Protect(value);

    public string Unprotect(string value)
    {
        try
        {
            return _protector.Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new ApiException(HttpStatusCode.Conflict, "The stored bank credentials can no longer be read; the bank must be connected again.");
        }
    }
}

/// <summary>US banking business days (weekends and federal holidays are not).</summary>
public static class BusinessCalendar
{
    private static readonly TimeZoneInfo Eastern = FindEastern();

    private static TimeZoneInfo FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public static DateTime TodayEastern() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Eastern).Date;

    public static bool IsBusinessDay(DateTime d) =>
        d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday && !IsHoliday(d);

    public static DateTime SameOrNextBusinessDay(DateTime d)
    {
        while (!IsBusinessDay(d)) d = d.AddDays(1);
        return d;
    }

    public static DateTime AddBusinessDays(DateTime d, int days)
    {
        d = SameOrNextBusinessDay(d);
        while (days > 0)
        {
            d = SameOrNextBusinessDay(d.AddDays(1));
            days--;
        }
        return d;
    }

    private static bool IsHoliday(DateTime d)
    {
        // Federal Reserve holidays; a Saturday holiday is not observed on Friday for banks.
        var candidates = new List<DateTime>
        {
            Observed(new DateTime(d.Year, 1, 1)),
            Nth(d.Year, 1, DayOfWeek.Monday, 3),
            Nth(d.Year, 2, DayOfWeek.Monday, 3),
            Last(d.Year, 5, DayOfWeek.Monday),
            Observed(new DateTime(d.Year, 6, 19)),
            Observed(new DateTime(d.Year, 7, 4)),
            Nth(d.Year, 9, DayOfWeek.Monday, 1),
            Nth(d.Year, 10, DayOfWeek.Monday, 2),
            Observed(new DateTime(d.Year, 11, 11)),
            Nth(d.Year, 11, DayOfWeek.Thursday, 4),
            Observed(new DateTime(d.Year, 12, 25)),
        };
        return candidates.Contains(d.Date);
    }

    private static DateTime Observed(DateTime d) => d.DayOfWeek == DayOfWeek.Sunday ? d.AddDays(1) : d;

    private static DateTime Nth(int year, int month, DayOfWeek day, int n)
    {
        var d = new DateTime(year, month, 1);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d.AddDays(7 * (n - 1));
    }

    private static DateTime Last(int year, int month, DayOfWeek day)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        while (d.DayOfWeek != day) d = d.AddDays(-1);
        return d;
    }
}
