using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Staff-side billing: monthly invoice schedules for business customers and seller (merchant) onboarding.
/// </summary>
public class BillingService
{
    // Stax only accepts a schedule that starts in the future, so "today" means a couple of minutes from now.
    private static readonly TimeSpan StartLead = TimeSpan.FromMinutes(2);

    private readonly StaxGateway _stax;
    private readonly IConfiguration _configuration;

    public BillingService(StaxGateway stax, IConfiguration configuration)
    {
        _stax = stax;
        _configuration = configuration;
    }

    private static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return new MailAddress(value).Address == value.Trim(); } catch (FormatException) { return false; }
    }

    // ================================================================ invoice schedules

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest request, CancellationToken ct)
    {
        if (!IsEmail(request.Email)) throw PaymentException.BadRequest("A valid customer email is required.");
        if (string.IsNullOrWhiteSpace(request.Company)) throw PaymentException.BadRequest("company is required.");
        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0) throw PaymentException.BadRequest("amount must be greater than zero.");

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset start;
        if (string.IsNullOrWhiteSpace(request.FirstInvoiceDate))
        {
            start = now + StartLead;
        }
        else
        {
            if (!DateTime.TryParseExact(request.FirstInvoiceDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                throw PaymentException.BadRequest("firstInvoiceDate must be a date in the form yyyy-MM-dd.");
            }
            if (day.Date < now.UtcDateTime.Date) throw PaymentException.BadRequest("firstInvoiceDate cannot be in the past.");
            start = day.Date == now.UtcDateTime.Date
                ? now + StartLead
                : new DateTimeOffset(day.Date.AddHours(12), TimeSpan.Zero);
        }

        var email = request.Email!.Trim();
        var company = request.Company!.Trim();
        var customer = (await _stax.FindCustomersByEmail(email, ct)).FirstOrDefault();
        string customerId;
        if (customer.ValueKind == JsonValueKind.Object)
        {
            customerId = StaxGateway.Str(customer, "id")!;
        }
        else
        {
            var created = await _stax.CreateCustomer(email, company, "Accounts", company, ct);
            customerId = StaxGateway.Str(created, "id")!;
        }

        var webBase = _configuration["baseUrls:webBase"];
        var body = new Dictionary<string, object?>
        {
            ["customer_id"] = customerId,
            ["url"] = string.IsNullOrWhiteSpace(webBase) ? "https://localhost/" : webBase,
            ["total"] = amount,
            ["meta"] = new Dictionary<string, object?> { ["subtotal"] = amount, ["tax"] = 0, ["reference"] = company },
            ["rule"] = BuildRule(start),
        };
        var schedule = await _stax.CreateInvoiceSchedule(body, ct);
        var dto = ToDto(schedule);
        dto.Email = email;
        dto.Company = company;
        return dto;
    }

    private static string BuildRule(DateTimeOffset start)
    {
        var dtstart = start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var day = start.UtcDateTime.Day;
        // Days 29-31: the chosen day, or the last day of shorter months.
        return day <= 28
            ? $"DTSTART={dtstart};FREQ=MONTHLY"
            : $"DTSTART={dtstart};FREQ=MONTHLY;BYMONTHDAY={day},-1;BYSETPOS=1";
    }

    public async Task<InvoiceScheduleDto> PauseScheduleAsync(string scheduleId, CancellationToken ct)
    {
        var current = await _stax.GetInvoiceSchedule(scheduleId, ct);
        if (StaxGateway.Bool(current, "active") == false) return ToDto(current);
        var updated = await _stax.UpdateInvoiceSchedule(scheduleId, new Dictionary<string, object?> { ["active"] = false }, ct);
        return ToDto(updated);
    }

    public async Task<InvoiceScheduleDto> ResumeScheduleAsync(string scheduleId, CancellationToken ct)
    {
        var current = await _stax.GetInvoiceSchedule(scheduleId, ct);
        if (StaxGateway.Bool(current, "active") == true) return ToDto(current);

        var rule = StaxGateway.Str(current, "rule");
        if (string.IsNullOrWhiteSpace(rule)) throw PaymentException.Unprocessable("The schedule has no recurrence rule to resume.");
        var next = NextOccurrence(rule!, DateTimeOffset.UtcNow);
        var newRule = Regex.Replace(rule!, @"DTSTART=\d{8}T\d{6}Z", "DTSTART=" + next.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        var updated = await _stax.UpdateInvoiceSchedule(scheduleId, new Dictionary<string, object?> { ["active"] = true, ["rule"] = newRule }, ct);
        return ToDto(updated);
    }

    /// <summary>The next monthly date, keeping the schedule's day of the month, that Stax will accept as a start (in the future).</summary>
    public static DateTimeOffset NextOccurrence(string rule, DateTimeOffset now)
    {
        var m = Regex.Match(rule, @"DTSTART=(\d{8}T\d{6})Z");
        if (!m.Success) throw PaymentException.Unprocessable("The schedule's recurrence rule has no DTSTART.");
        var start = DateTime.ParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var dayMatch = Regex.Match(rule, @"BYMONTHDAY=(\d+)");
        var day = dayMatch.Success ? int.Parse(dayMatch.Groups[1].Value, CultureInfo.InvariantCulture) : start.Day;

        var candidate = start;
        var offset = 0;
        while (candidate <= now.UtcDateTime + StartLead)
        {
            offset++;
            var month = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(offset);
            var d = Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month));
            candidate = new DateTime(month.Year, month.Month, d, start.Hour, start.Minute, start.Second, DateTimeKind.Utc);
        }
        return new DateTimeOffset(candidate, TimeSpan.Zero);
    }

    private static InvoiceScheduleDto ToDto(JsonElement s)
    {
        var active = StaxGateway.Bool(s, "active") ?? false;
        var customer = s.ValueKind == JsonValueKind.Object && s.TryGetProperty("customer", out var c) ? c : default;
        return new InvoiceScheduleDto
        {
            ScheduleId = StaxGateway.Str(s, "id") ?? string.Empty,
            CustomerId = StaxGateway.Str(s, "customer_id"),
            Email = customer.ValueKind == JsonValueKind.Object ? StaxGateway.Str(customer, "email") : null,
            Company = customer.ValueKind == JsonValueKind.Object ? StaxGateway.Str(customer, "company") : null,
            Amount = StaxGateway.Dec(s, "total") ?? 0m,
            Active = active,
            Status = active ? "active" : "paused",
            NextInvoiceAt = StaxGateway.Str(s, "next_run_at"),
            Rule = StaxGateway.Str(s, "rule"),
        };
    }

    // ================================================================ sellers

    public async Task<SellerDto> OnboardSellerAsync(OnboardSellerRequest request, CancellationToken ct)
    {
        var company = request.Company?.Trim();
        var name = request.Contact?.Name ?? request.ContactName;
        var email = request.Contact?.Email ?? request.ContactEmail;
        var phone = request.Contact?.Phone ?? request.ContactPhone;
        if (string.IsNullOrWhiteSpace(company)) throw PaymentException.BadRequest("company is required.");
        if (!IsEmail(email)) throw PaymentException.BadRequest("A valid contact email is required (contact.email).");

        var body = new Dictionary<string, object?>
        {
            ["company_name"] = company,
            ["contact_email"] = email!.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(name)) body["contact_name"] = name!.Trim();
        if (!string.IsNullOrWhiteSpace(phone)) body["contact_phone"] = phone!.Trim();

        var merchant = await _stax.CreateMerchant(body, ct);
        return new SellerDto
        {
            SellerId = StaxGateway.Str(merchant, "id") ?? string.Empty,
            Company = StaxGateway.Str(merchant, "company_name") ?? company,
            Status = StaxGateway.Str(merchant, "status"),
            ContactName = StaxGateway.Str(merchant, "contact_name") ?? name,
            ContactEmail = StaxGateway.Str(merchant, "contact_email") ?? email,
        };
    }
}
