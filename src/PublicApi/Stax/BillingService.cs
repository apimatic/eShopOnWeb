using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public record InvoiceScheduleView(string ScheduleId, string Status, bool Active, string CustomerEmail, string Company,
    decimal Amount, DateOnly FirstInvoiceDate, DateOnly? NextInvoiceDate);

public record SellerView(int SellerId, string MerchantId, string Company, string ContactName, string ContactEmail);

/// <summary>Staff-only billing: monthly invoice schedules for business customers, and marketplace sellers.</summary>
public class BillingService
{
    private const string MonthlyRule = "FREQ=MONTHLY;INTERVAL=1";
    private const string MonthEndRule = "FREQ=MONTHLY;BYMONTHDAY=-1";

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly IOptions<BaseUrlConfiguration> _urls;

    public BillingService(CatalogContext db, StaxGateway stax, IOptions<BaseUrlConfiguration> urls)
    {
        _db = db;
        _stax = stax;
        _urls = urls;
    }

    private static DateTime Today => DateTime.UtcNow.Date;

    private static bool IsLastDayOfMonth(DateTime d) => d.Day == DateTime.DaysInMonth(d.Year, d.Month);

    private static string RuleFor(InvoiceScheduleRecord r) => r.DayOfMonth > 28 ? MonthEndRule : MonthlyRule;

    private static string Stamp(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00";

    // ------------------------------------------------------------------ invoice schedules

    public async Task<InvoiceScheduleView> CreateScheduleAsync(string? email, string? company, decimal? amount, DateOnly? firstInvoiceDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || !IsEmail(email))
            throw PaymentRequestException.BadRequest("customerEmail must be a valid e-mail address.");
        if (string.IsNullOrWhiteSpace(company))
            throw PaymentRequestException.BadRequest("company is required.");
        if (amount is null or <= 0m || decimal.Round(amount.Value, 2) != amount.Value)
            throw PaymentRequestException.BadRequest("amount must be a positive amount with at most two decimals.");

        var first = firstInvoiceDate is { } chosen ? chosen.ToDateTime(TimeOnly.MinValue) : Today;
        if (first < Today)
            throw PaymentRequestException.BadRequest("The first invoice day cannot be in the past.");
        if (first.Day > 28 && !IsLastDayOfMonth(first))
            throw PaymentRequestException.BadRequest(
                "A monthly invoice cannot start on the 29th or 30th, because not every month has that day. Pick a day from the 1st to the 28th, or the last day of a month.");

        var customer = await _stax.FindOrCreateCustomerAsync(new Dictionary<string, object?>
        {
            ["email"] = email.Trim(),
            ["company"] = company.Trim()
        }, ct);
        var customerId = customer.Str("id") ?? throw new StaxApiException(0, "Stax did not return a customer id.");
        if (!string.Equals(customer.Str("company"), company.Trim(), StringComparison.Ordinal))
        {
            await _stax.UpdateCustomerAsync(customerId, new Dictionary<string, object?> { ["company"] = company.Trim() }, ct);
        }

        var record = new InvoiceScheduleRecord
        {
            StaxCustomerId = customerId,
            CustomerEmail = email.Trim(),
            Company = company.Trim(),
            Amount = amount.Value,
            FirstInvoiceDate = first,
            DayOfMonth = first.Day > 28 ? 31 : first.Day
        };

        var created = await _stax.CreateInvoiceScheduleAsync(new Dictionary<string, object?>
        {
            ["url"] = (_urls.Value.WebBase ?? "https://localhost").TrimEnd('/') + "/",
            ["total"] = amount.Value,
            ["customer_id"] = customerId,
            ["meta"] = new Dictionary<string, object?>
            {
                ["lineItems"] = new[]
                {
                    new Dictionary<string, object?> { ["item"] = "Monthly invoice", ["price"] = amount.Value, ["quantity"] = 1 }
                }
            }
        }, ct);
        var scheduleId = created.Str("id") ?? throw new StaxApiException(0, "Stax did not return a schedule id.");
        record.StaxScheduleId = scheduleId;

        JsonElement started;
        try
        {
            // Stax works out the first run from the rule, so the rule is set first and the first day after it
            await _stax.UpdateInvoiceScheduleAsync(scheduleId, new Dictionary<string, object?> { ["rule"] = RuleFor(record), ["active"] = true }, ct);
            started = await _stax.UpdateInvoiceScheduleAsync(scheduleId, new Dictionary<string, object?> { ["next_run_at"] = Stamp(first) }, ct);
        }
        catch
        {
            await TryDeleteAsync(scheduleId);
            throw;
        }

        _db.InvoiceSchedules.Add(record);
        await _db.SaveChangesAsync(ct);
        return ToView(record, started);
    }

    private async Task TryDeleteAsync(string scheduleId)
    {
        try
        {
            await _stax.DeleteInvoiceScheduleAsync(scheduleId);
        }
        catch (StaxApiException)
        {
        }
    }

    private static bool IsEmail(string value)
    {
        return MailAddress.TryCreate(value.Trim(), out var a) && a.Address == value.Trim() && value.Contains('@');
    }

    private async Task<InvoiceScheduleRecord> FindAsync(string scheduleId, CancellationToken ct) =>
        await _db.InvoiceSchedules.FirstOrDefaultAsync(s => s.StaxScheduleId == scheduleId, ct)
        ?? throw PaymentRequestException.NotFound("No such invoice schedule.");

    public async Task<InvoiceScheduleView> PauseAsync(string scheduleId, CancellationToken ct)
    {
        var record = await FindAsync(scheduleId, ct);
        var result = await _stax.UpdateInvoiceScheduleAsync(record.StaxScheduleId, new Dictionary<string, object?> { ["active"] = false }, ct);
        record.Active = false;
        await _db.SaveChangesAsync(ct);
        return ToView(record, result);
    }

    public async Task<InvoiceScheduleView> ResumeAsync(string scheduleId, CancellationToken ct)
    {
        var record = await FindAsync(scheduleId, ct);

        // the next invoice day on or after today, never before the first one and never a day already invoiced
        var current = await _stax.GetInvoiceScheduleAsync(record.StaxScheduleId, ct);
        var lastRun = current.ValueKind == JsonValueKind.Object ? current.Time("last_run_at")?.UtcDateTime.Date : null;
        var next = NextOccurrence(record, Today);
        while (lastRun is { } last && next <= last) next = NextOccurrence(record, next.AddDays(1));

        await _stax.UpdateInvoiceScheduleAsync(record.StaxScheduleId, new Dictionary<string, object?> { ["rule"] = RuleFor(record), ["active"] = true }, ct);
        var result = await _stax.UpdateInvoiceScheduleAsync(record.StaxScheduleId, new Dictionary<string, object?> { ["next_run_at"] = Stamp(next) }, ct);
        record.Active = true;
        await _db.SaveChangesAsync(ct);
        return ToView(record, result);
    }

    /// <summary>The first invoice day of the schedule on or after <paramref name="from"/>.</summary>
    private static DateTime NextOccurrence(InvoiceScheduleRecord record, DateTime from)
    {
        var day = from.Date < record.FirstInvoiceDate.Date ? record.FirstInvoiceDate.Date : from.Date;
        if (record.DayOfMonth > 28)
        {
            return new DateTime(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));
        }
        var candidate = new DateTime(day.Year, day.Month, record.DayOfMonth);
        return candidate >= day ? candidate : candidate.AddMonths(1);
    }

    private static InvoiceScheduleView ToView(InvoiceScheduleRecord r, JsonElement stax)
    {
        DateOnly? next = stax.ValueKind == JsonValueKind.Object && stax.Time("next_run_at") is { } n ? DateOnly.FromDateTime(n.UtcDateTime) : null;
        var status = r.Active ? "active" : "paused";
        return new InvoiceScheduleView(r.StaxScheduleId, status, r.Active, r.CustomerEmail, r.Company, r.Amount, DateOnly.FromDateTime(r.FirstInvoiceDate), r.Active ? next : null);
    }

    // ------------------------------------------------------------------ sellers

    public async Task<SellerView> OnboardSellerAsync(string? company, string? contactName, string? contactEmail, string? contactPhone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(company))
            throw PaymentRequestException.BadRequest("company is required.");
        if (string.IsNullOrWhiteSpace(contactName))
            throw PaymentRequestException.BadRequest("contactName is required.");
        if (string.IsNullOrWhiteSpace(contactEmail) || !IsEmail(contactEmail))
            throw PaymentRequestException.BadRequest("contactEmail must be a valid e-mail address.");
        if (!_stax.HasPlatformKey)
            throw PaymentRequestException.Unprocessable(
                "Sellers cannot be onboarded: this shop has no Stax platform key (Stax:PlatformApiKey) configured.");

        var body = new Dictionary<string, object?>
        {
            ["company_name"] = company.Trim(),
            ["contact_name"] = contactName.Trim(),
            ["contact_email"] = contactEmail.Trim()
        };
        if (!string.IsNullOrWhiteSpace(contactPhone)) body["contact_phone"] = contactPhone.Trim();

        var merchant = await _stax.CreateMerchantAsync(body, ct);
        var merchantId = merchant.Str("id") ?? throw new StaxApiException(0, "Stax did not return a merchant id.");
        var record = new SellerRecord
        {
            StaxMerchantId = merchantId,
            Company = company.Trim(),
            ContactName = contactName.Trim(),
            ContactEmail = contactEmail.Trim()
        };
        _db.Sellers.Add(record);
        await _db.SaveChangesAsync(ct);
        return new SellerView(record.Id, merchantId, record.Company, record.ContactName, record.ContactEmail);
    }
}
