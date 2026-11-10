using System.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>
/// The app cannot carry out the request; surfaces as a 4xx with a reason.
/// </summary>
public class BankApiException : Exception
{
    public BankApiException(HttpStatusCode status, string reason) : base(reason)
    {
        Status = status;
    }

    public HttpStatusCode Status { get; }

    public static BankApiException BadRequest(string reason) => new(HttpStatusCode.BadRequest, reason);
    public static BankApiException NotFound(string reason) => new(HttpStatusCode.NotFound, reason);
    public static BankApiException Conflict(string reason) => new(HttpStatusCode.Conflict, reason);
    public static BankApiException Unprocessable(string reason) => new((HttpStatusCode)422, reason);
}

public static class PlaidErrorMapper
{
    /// <summary>Maps an error from Plaid to the status and reason the shop's API answers with.</summary>
    public static (HttpStatusCode Status, string Reason) Map(PlaidException ex)
    {
        var reason = ex.DisplayMessage ?? ex.Message;
        switch (ex.ErrorCode)
        {
            case "PRODUCT_NOT_READY":
                return (HttpStatusCode.Conflict, "The bank's data is not ready yet; please try again shortly.");
            case "RATE_LIMIT_EXCEEDED":
                return ((HttpStatusCode)429, "Too many requests to the bank provider; please try again shortly.");
            case "ITEM_NOT_FOUND":
            case "INVALID_ACCESS_TOKEN":
                return (HttpStatusCode.Conflict, "This connection is no longer valid; please reconnect the account.");
            case "ITEM_LOGIN_REQUIRED":
                return (HttpStatusCode.Conflict, "The bank needs the shopper to sign in again; please reconnect the account.");
            case "ADDITIONAL_CONSENT_REQUIRED":
                return (HttpStatusCode.Conflict, "More consent is needed from the shopper for this data; please reconnect the account.");
            case "NO_ACCOUNTS":
            case "NO_INVESTMENT_ACCOUNTS":
                return (HttpStatusCode.Conflict, reason);
            case "INVALID_PUBLIC_TOKEN":
                return (HttpStatusCode.BadRequest, "The public token is invalid or has expired.");
        }

        return ex.ErrorType switch
        {
            "INVALID_REQUEST" or "INVALID_INPUT" => (HttpStatusCode.BadRequest, reason),
            "ITEM_ERROR" or "INSTITUTION_ERROR" => (HttpStatusCode.Conflict, reason),
            "TRANSFER_ERROR" or "TRANSFER_REFUND_ERROR" or "TRANSFER_LIMIT_ERROR" => ((HttpStatusCode)422, reason),
            _ => (HttpStatusCode.BadGateway, "The bank provider could not process the request: " + reason)
        };
    }
}

public static class Shopper
{
    /// <summary>The caller's identity, from the JWT. Normalized so it compares case-insensitively.</summary>
    public static string? IdOf(ClaimsPrincipal? user)
    {
        var name = user?.Identity?.Name ?? user?.FindFirstValue(ClaimTypes.Name);
        return Normalize(name);
    }

    public static string Require(ClaimsPrincipal? user) =>
        IdOf(user) ?? throw new BankApiException(HttpStatusCode.Unauthorized, "The token does not identify a user.");

    public static string? Normalize(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToLowerInvariant();
}

/// <summary>
/// Keyed async locks (single instance), used to serialize work per connection / checkout.
/// </summary>
public class KeyedLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

/// <summary>
/// Protects Plaid access tokens before they are stored.
/// </summary>
public class AccessTokenProtector
{
    private readonly IDataProtector _protector;

    public AccessTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("eShopOnWeb.Plaid.AccessToken.v1");
    }

    public string Protect(string token) => _protector.Protect(token);
    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}

/// <summary>
/// US business-day arithmetic (weekends and federal holidays) in Eastern time.
/// </summary>
public static class BusinessCalendar
{
    public static DateTime TodayEastern(TimeProvider clock)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.CreateCustomTimeZone("ET", TimeSpan.FromHours(-5), "ET", "ET"); }
        }
        return TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date;
    }

    public static bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday && !IsFederalHoliday(date);

    public static DateTime OnOrAfter(DateTime date)
    {
        date = date.Date;
        while (!IsBusinessDay(date)) date = date.AddDays(1);
        return date;
    }

    public static DateTime AddBusinessDays(DateTime date, int days)
    {
        date = date.Date;
        while (days > 0)
        {
            date = date.AddDays(1);
            if (IsBusinessDay(date)) days--;
        }
        return date;
    }

    private static bool IsFederalHoliday(DateTime d)
    {
        // A holiday on Saturday is observed Friday, one on Sunday is observed Monday.
        foreach (var holiday in Holidays(d.Year).Concat(Holidays(d.Year + 1)))
        {
            var observed = holiday.DayOfWeek switch
            {
                DayOfWeek.Saturday => holiday.AddDays(-1),
                DayOfWeek.Sunday => holiday.AddDays(1),
                _ => holiday
            };
            if (observed == d) return true;
        }
        return false;
    }

    private static IEnumerable<DateTime> Holidays(int y)
    {
        yield return new DateTime(y, 1, 1);
        yield return NthWeekday(y, 1, DayOfWeek.Monday, 3);   // Martin Luther King Jr. Day
        yield return NthWeekday(y, 2, DayOfWeek.Monday, 3);   // Presidents Day
        yield return LastWeekday(y, 5, DayOfWeek.Monday);     // Memorial Day
        yield return new DateTime(y, 6, 19);                  // Juneteenth
        yield return new DateTime(y, 7, 4);
        yield return NthWeekday(y, 9, DayOfWeek.Monday, 1);   // Labor Day
        yield return NthWeekday(y, 10, DayOfWeek.Monday, 2);  // Columbus Day
        yield return new DateTime(y, 11, 11);                 // Veterans Day
        yield return NthWeekday(y, 11, DayOfWeek.Thursday, 4); // Thanksgiving
        yield return new DateTime(y, 12, 25);
    }

    private static DateTime NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var d = new DateTime(year, month, 1);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d.AddDays(7 * (n - 1));
    }

    private static DateTime LastWeekday(int year, int month, DayOfWeek day)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        while (d.DayOfWeek != day) d = d.AddDays(-1);
        return d;
    }
}
