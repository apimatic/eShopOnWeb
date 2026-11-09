using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateInvoiceScheduleRequest
{
    public string? CustomerEmail { get; set; }
    public string? Email { get => CustomerEmail; set => CustomerEmail ??= value; }
    public string? Company { get; set; }
    public decimal? Amount { get; set; }

    /// <summary>The day of the first invoice (yyyy-MM-dd). Today when left out.</summary>
    public string? FirstInvoiceDate { get; set; }
    public string? StartDate { get => FirstInvoiceDate; set => FirstInvoiceDate ??= value; }
    public string? FirstInvoiceDay { get => FirstInvoiceDate; set => FirstInvoiceDate ??= value; }
}

public record InvoiceScheduleDto(
    string ScheduleId,
    string? CustomerEmail,
    string? Company,
    decimal Amount,
    string Status,
    bool Active,
    string? NextInvoiceAt,
    string? LastInvoiceAt,
    int InvoicesIssued,
    string? Repeats);

public class InvoiceScheduleService
{
    // Where Stax sends the customer to see and pay an invoice.
    private const string InvoiceUrl = "https://omni.fattmerchant.com/#/bill/";

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;

    public InvoiceScheduleService(CatalogContext db, StaxGateway stax)
    {
        _db = db;
        _stax = stax;
    }

    public async Task<InvoiceScheduleDto> Create(CreateInvoiceScheduleRequest request, CancellationToken ct)
    {
        var email = request.CustomerEmail?.Trim();
        if (string.IsNullOrEmpty(email) || !IsEmail(email))
            throw AppException.BadRequest("customerEmail must be a valid e-mail address.");
        var company = request.Company?.Trim();
        if (string.IsNullOrEmpty(company))
            throw AppException.BadRequest("company is required.");
        if (request.Amount is not > 0m || request.Amount > 10_000_000m || decimal.Round(request.Amount.Value, 2) != request.Amount.Value)
            throw AppException.BadRequest("amount must be greater than zero and have at most two decimals.");

        var today = DateTime.UtcNow.Date;
        var first = today;
        if (!string.IsNullOrWhiteSpace(request.FirstInvoiceDate))
        {
            if (!DateTime.TryParse(request.FirstInvoiceDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                throw AppException.BadRequest("firstInvoiceDate must be a date like 2026-12-01.");
            first = parsed.Date;
            if (first < today)
                throw AppException.BadRequest("firstInvoiceDate cannot be in the past.");
        }

        var customerId = await FindOrCreateBusinessCustomer(email, company, ct);
        var rule = Rule(first, first == today);
        var created = await _stax.CreateSchedule(new Dictionary<string, object?>
        {
            ["url"] = InvoiceUrl,
            ["total"] = request.Amount.Value,
            ["customer_id"] = customerId,
            ["rule"] = rule,
            ["meta"] = new Dictionary<string, object?> { ["memo"] = $"Monthly invoice for {company}", ["subtotal"] = request.Amount.Value }
        }, ct);

        var id = StaxGateway.Str(created, "id") ?? throw new AppException(HttpStatusCode.BadGateway, "Stax did not return the new schedule.");
        _db.InvoiceSchedules.Add(new InvoiceScheduleRecord
        {
            Id = id,
            CustomerEmail = email.ToLowerInvariant(),
            Company = company,
            StaxCustomerId = customerId,
            Amount = request.Amount.Value,
            FirstInvoiceDate = first,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return await ToDto(id, created, ct);
    }

    public async Task<InvoiceScheduleDto> Pause(string scheduleId, CancellationToken ct)
    {
        var current = await Load(scheduleId, ct);
        if (!StaxGateway.Bool(current, "active"))
            throw AppException.Conflict("The schedule is already paused.");
        var updated = await _stax.UpdateSchedule(scheduleId, new { active = false }, ct);
        return await ToDto(scheduleId, updated, ct);
    }

    public async Task<InvoiceScheduleDto> Resume(string scheduleId, CancellationToken ct)
    {
        var current = await Load(scheduleId, ct);
        if (StaxGateway.Bool(current, "active"))
            throw AppException.Conflict("The schedule is not paused.");

        // Stax continues from the next occurrence of the rule. A schedule that was paused before its
        // first invoice went out would silently lose that invoice, so start it again from now.
        var neverRan = StaxGateway.Int(current, "count") == 0 && StaxGateway.Str(current, "last_run_at") == null;
        var due = StartOf(StaxGateway.Str(current, "rule"));
        JsonElement updated;
        if (neverRan && due != null && due <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            var rule = Rule(DateTime.UtcNow.Date, today: true, StaxRuleDay(StaxGateway.Str(current, "rule")));
            updated = await _stax.UpdateSchedule(scheduleId, new { active = true, rule }, ct);
        }
        else
        {
            updated = await _stax.UpdateSchedule(scheduleId, new { active = true }, ct);
        }
        return await ToDto(scheduleId, updated, ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<JsonElement> Load(string scheduleId, CancellationToken ct)
    {
        if (!Guid.TryParse(scheduleId, out _))
            throw AppException.NotFound("No such invoice schedule.");
        try
        {
            var s = await _stax.GetSchedule(scheduleId, ct);
            if (s.ValueKind != JsonValueKind.Object || StaxGateway.Str(s, "deleted_at") != null)
                throw AppException.NotFound("No such invoice schedule.");
            return s;
        }
        catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound || e.StaxStatus == 422)
        {
            throw AppException.NotFound("No such invoice schedule.");
        }
    }

    private async Task<string> FindOrCreateBusinessCustomer(string email, string company, CancellationToken ct)
    {
        var found = (await _stax.FindCustomersByEmail(email, ct))
            .FirstOrDefault(c => StaxGateway.Str(c, "deleted_at") == null &&
                                 string.Equals(StaxGateway.Str(c, "company"), company, StringComparison.OrdinalIgnoreCase));
        if (found.ValueKind == JsonValueKind.Object)
            return StaxGateway.Str(found, "id")!;
        var created = await _stax.CreateCustomer(email, company, "Accounts", company, ct);
        return StaxGateway.Str(created, "id") ?? throw new AppException(HttpStatusCode.BadGateway, "Stax did not return the new customer.");
    }

    /// <summary>
    /// The monthly rule. Stax refuses a start in the past, so "today" starts a moment from now.
    /// Starting on the 29th-31st means "that day, or the last day of shorter months".
    /// </summary>
    private static string Rule(DateTime day, bool today, int? dayOfMonth = null)
    {
        var start = today ? DateTime.UtcNow.AddSeconds(30) : day;
        if (today && start.Date != day.Date) start = day.AddDays(1).AddSeconds(-1);
        var dom = dayOfMonth ?? day.Day;
        var stamp = start.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var by = dom >= 29 ? $"BYMONTHDAY={string.Join(",", Enumerable.Range(dom, 32 - dom))},-1;BYSETPOS=1;" : string.Empty;
        return $"FREQ=MONTHLY;{by}DTSTART={stamp}";
    }

    private static int? StaxRuleDay(string? rule)
    {
        // keep a late-month anchor (29th-31st) when the rule had one
        var marker = "BYMONTHDAY=";
        var i = rule?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
        if (i < 0) return null;
        var digits = new string(rule!.Skip(i + marker.Length).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var d) ? d : null;
    }

    private static DateTimeOffset? StartOf(string? rule)
    {
        var marker = "DTSTART=";
        var i = rule?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
        if (i < 0) return null;
        var stamp = new string(rule!.Skip(i + marker.Length).TakeWhile(c => char.IsLetterOrDigit(c)).ToArray());
        return DateTimeOffset.TryParseExact(stamp, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }

    private async Task<InvoiceScheduleDto> ToDto(string id, JsonElement s, CancellationToken ct)
    {
        var record = await _db.InvoiceSchedules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        var customer = s.ValueKind == JsonValueKind.Object && s.TryGetProperty("customer", out var c) ? c : default;
        return new InvoiceScheduleDto(
            id,
            record?.CustomerEmail ?? StaxGateway.Str(customer, "email"),
            record?.Company ?? StaxGateway.Str(customer, "company"),
            record?.Amount ?? StaxGateway.Dec(s, "total") ?? 0m,
            StaxGateway.Str(s, "status") ?? string.Empty,
            StaxGateway.Bool(s, "active"),
            StaxGateway.Str(s, "next_run_at"),
            StaxGateway.Str(s, "last_run_at"),
            StaxGateway.Int(s, "count"),
            StaxGateway.Str(s, "rule_text"));
    }

    private static bool IsEmail(string value)
    {
        try { return new MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
    }
}
