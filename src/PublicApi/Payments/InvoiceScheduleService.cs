using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class InvoiceScheduleService
{
    private const string HostedInvoiceUrl = "https://app.staxpayments.com/#/bill/";
    private static readonly TimeSpan StartLead = TimeSpan.FromMinutes(3);

    private readonly StaxContext _db;
    private readonly StaxClient _stax;

    public InvoiceScheduleService(StaxContext db, StaxClient stax)
    {
        _db = db;
        _stax = stax;
    }

    public static bool IsValidEmail(string email)
    {
        try { return MailAddress.TryCreate(email, out var a) && a.Address == email && email.Contains('.', StringComparison.Ordinal); }
        catch { return false; }
    }

    public async Task<InvoiceScheduleRecord> CreateAsync(string email, string company, decimal amount,
        DateTime? startDate, CancellationToken ct)
    {
        if (!IsValidEmail(email)) throw ApiException.BadRequest("'email' must be a valid email address.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw ApiException.BadRequest("'amount' must be above zero, with at most two decimals.");

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset first;
        if (startDate == null || startDate.Value.Date == now.UtcDateTime.Date)
        {
            // "Today": Stax only accepts a start that is still ahead, so the first invoice goes out in minutes.
            first = now + StartLead;
        }
        else if (startDate.Value.Date < now.UtcDateTime.Date)
        {
            throw ApiException.BadRequest("'startDate' cannot be in the past.");
        }
        else
        {
            first = new DateTimeOffset(startDate.Value.Date.AddHours(12), TimeSpan.Zero);
        }
        var day = first.UtcDateTime.Day;

        var customerId = await FindOrCreateCustomerAsync(email, company, ct);
        var created = await _stax.CreateScheduleAsync(new
        {
            url = HostedInvoiceUrl,
            total = amount,
            customer_id = customerId,
            rule = BuildRule(first, day),
            email_notification = true,
        }, ct);

        var record = new InvoiceScheduleRecord
        {
            Id = created["id"]!.GetValue<string>(),
            CustomerEmail = email,
            Company = company,
            StaxCustomerId = customerId,
            Amount = amount,
            FirstInvoiceDate = first.UtcDateTime.Date,
            DayOfMonth = day,
            Active = true,
            CreatedAt = now,
        };
        _db.InvoiceSchedules.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    /// <summary>
    /// Monthly on the same day. A start on the 29th-31st would silently skip shorter months with a plain
    /// monthly rule, so those days fall back to the month's last day instead.
    /// </summary>
    public static string BuildRule(DateTimeOffset start, int day)
    {
        var rule = $"DTSTART={start.UtcDateTime:yyyyMMdd'T'HHmmss'Z'};FREQ=MONTHLY";
        if (day >= 29)
        {
            rule += ";BYMONTHDAY=" + string.Join(",", Enumerable.Range(28, day - 27)) + ";BYSETPOS=-1";
        }
        return rule;
    }

    private async Task<string> FindOrCreateCustomerAsync(string email, string company, CancellationToken ct)
    {
        var shopperCustomers = await _db.ShopperAccounts.Select(a => a.StaxCustomerId).ToListAsync(ct);
        foreach (var id in await _stax.FindCustomerIdsByEmailAsync(email, ct))
        {
            if (shopperCustomers.Contains(id)) continue;
            var existing = await _db.InvoiceSchedules.FirstOrDefaultAsync(
                s => s.StaxCustomerId == id && s.Company == company, ct);
            if (existing != null) return id;
        }
        return await _stax.CreateCustomerAsync(new { email, company, reference = "eshop-business" }, ct);
    }

    public async Task<InvoiceScheduleRecord> RequireAsync(string id, CancellationToken ct)
        => await _db.InvoiceSchedules.FirstOrDefaultAsync(s => s.Id == id, ct)
           ?? throw ApiException.NotFound("There is no such invoice schedule.");

    public async Task<InvoiceScheduleRecord> PauseAsync(string id, CancellationToken ct)
    {
        var record = await RequireAsync(id, ct);
        if (record.Active)
        {
            await _stax.UpdateScheduleAsync(id, new { active = false }, ct);
            record.Active = false;
            await _db.SaveChangesAsync(ct);
        }
        return record;
    }

    public async Task<InvoiceScheduleRecord> ResumeAsync(string id, CancellationToken ct)
    {
        var record = await RequireAsync(id, ct);
        if (!record.Active)
        {
            // Stax needs a start date that is still ahead when a schedule is resumed.
            var current = await _stax.GetScheduleAsync(id, ct);
            var oldRule = current["rule"]?.GetValue<string>() ?? "";
            var notBefore = DateTimeOffset.UtcNow + StartLead;
            // A schedule that has not produced its first invoice yet keeps its original start date.
            var original = StartOf(oldRule);
            var next = original is { } o && o >= notBefore
                ? o
                : NextOccurrence(record.DayOfMonth, TimeOfDay(oldRule), notBefore);
            await _stax.UpdateScheduleAsync(id, new { active = true, rule = BuildRule(next, record.DayOfMonth) }, ct);
            record.Active = true;
            await _db.SaveChangesAsync(ct);
        }
        return record;
    }

    private static DateTimeOffset? StartOf(string rule)
    {
        var m = Regex.Match(rule, @"DTSTART=(\d{8}T\d{6})Z");
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero)
            : null;
    }

    private static TimeSpan TimeOfDay(string rule)
    {
        var m = Regex.Match(rule, @"DTSTART=\d{8}T(\d{2})(\d{2})(\d{2})Z");
        return m.Success
            ? new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : TimeSpan.FromHours(12);
    }

    /// <summary>The next date on or after <paramref name="notBefore"/> that falls on the billing day.</summary>
    public static DateTimeOffset NextOccurrence(int day, TimeSpan timeOfDay, DateTimeOffset notBefore)
    {
        var month = new DateTime(notBefore.UtcDateTime.Year, notBefore.UtcDateTime.Month, 1);
        for (var i = 0; i < 14; i++)
        {
            var d = Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month));
            var candidate = new DateTimeOffset(new DateTime(month.Year, month.Month, d) + timeOfDay, TimeSpan.Zero);
            if (candidate >= notBefore) return candidate;
            month = month.AddMonths(1);
        }
        return notBefore;
    }

    public static object ToDto(InvoiceScheduleRecord r) => new
    {
        scheduleId = r.Id,
        customerEmail = r.CustomerEmail,
        company = r.Company,
        amount = r.Amount,
        firstInvoiceDate = r.FirstInvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        dayOfMonth = r.DayOfMonth,
        status = r.Active ? "active" : "paused",
        active = r.Active,
    };
}
