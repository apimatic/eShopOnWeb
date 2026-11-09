using System;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateInvoiceScheduleRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Day of the first invoice (yyyy-MM-dd). Today when omitted.</summary>
    public string? FirstInvoiceDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? NextInvoiceAt { get; set; }
}

public class CreateSellerRequest
{
    public string? Company { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
}

public class SellerDto
{
    public string MerchantId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string Company { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
}

public class InvoiceAndSellerService
{
    private const string InvoicePageUrl = "https://app.staxpayments.com/#/bill/";

    private readonly StaxClient _stax;
    private readonly PaymentMethodService _methods;

    public InvoiceAndSellerService(StaxClient stax, PaymentMethodService methods)
    {
        _stax = stax;
        _methods = methods;
    }

    // ---------------------------------------------------------------- monthly invoices

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest r, CancellationToken ct)
    {
        var email = ValidEmail(r.Email, "email");
        if (string.IsNullOrWhiteSpace(r.Company)) throw ApiException.BadRequest("company is required.");
        if (r.Amount <= 0 || Math.Round(r.Amount, 2) != r.Amount) throw ApiException.BadRequest("amount must be greater than zero with at most two decimals.");

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset start;
        if (string.IsNullOrWhiteSpace(r.FirstInvoiceDate))
        {
            start = now.AddMinutes(2); // Stax only accepts a start in the future
        }
        else
        {
            if (!DateTime.TryParseExact(r.FirstInvoiceDate.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var day))
                throw ApiException.BadRequest("firstInvoiceDate must be a date like 2026-12-01.");
            if (day.Date < now.UtcDateTime.Date) throw ApiException.BadRequest("firstInvoiceDate cannot be in the past.");
            start = day.Date == now.UtcDateTime.Date
                ? now.AddMinutes(2)
                : new DateTimeOffset(day.Date.AddHours(12), TimeSpan.Zero);
        }

        var customerId = await EnsureBusinessCustomerAsync(email, r.Company.Trim(), ct);
        var created = await _stax.PostAsync("invoice/schedule", new
        {
            customer_id = customerId,
            total = r.Amount,
            url = InvoicePageUrl,
            rule = $"DTSTART={start:yyyyMMdd'T'HHmmss'Z'};FREQ=MONTHLY",
            meta = new { subtotal = r.Amount, tax = 0, memo = $"Monthly invoice for {r.Company.Trim()}" }
        }, ct);

        var id = created.Str("id") ?? throw new ApiException(502, "Stax did not return a schedule id.");
        var schedule = await _stax.GetAsync($"invoice/schedule/{id}", ct);
        return ToDto(schedule, r.Company.Trim());
    }

    private async Task<string> EnsureBusinessCustomerAsync(string email, string company, CancellationToken ct)
    {
        var existing = (await _methods.FindCustomersAsync(email, ct)).FirstOrDefault();
        if (existing != null)
        {
            var id = existing.Str("id")!;
            if (!string.Equals(existing.Str("company"), company, StringComparison.Ordinal))
                await _stax.PutAsync($"customer/{id}", new { company }, ct);
            return id;
        }
        var created = await _stax.PostAsync("customer", new { firstname = company, lastname = "Billing", company, email }, ct);
        return created.Str("id")!;
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string scheduleId, CancellationToken ct)
    {
        var schedule = await GetScheduleAsync(scheduleId, ct);
        if (!schedule.Bool("active")) throw ApiException.Conflict("The schedule is already paused.");
        var updated = await _stax.PutAsync($"invoice/schedule/{scheduleId}", new { active = false }, ct);
        return ToDto(updated, null);
    }

    public async Task<InvoiceScheduleDto> ResumeAsync(string scheduleId, CancellationToken ct)
    {
        var schedule = await GetScheduleAsync(scheduleId, ct);
        if (schedule.Bool("active")) throw ApiException.Conflict("The schedule is not paused.");

        // Stax resumes from the rule's start date, which must be in the future, so the schedule is moved on to
        // its next monthly occurrence instead of billing for the paused months.
        var rule = schedule.Str("rule") ?? throw new ApiException(502, "Stax returned a schedule without a rule.");
        var m = Regex.Match(rule, @"DTSTART=(\d{8}T\d{6}Z)");
        if (!m.Success) throw ApiException.Unprocessable("The schedule's recurrence rule cannot be resumed.");
        var anchor = DateTimeOffset.ParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        var floor = DateTimeOffset.UtcNow.AddMinutes(5);
        var next = anchor;
        for (var k = 1; next <= floor; k++) next = anchor.AddMonths(k);

        var newRule = rule.Replace(m.Groups[1].Value, next.ToString("yyyyMMdd'T'HHmmss'Z'"));
        var updated = await _stax.PutAsync($"invoice/schedule/{scheduleId}", new { active = true, rule = newRule }, ct);
        return ToDto(updated, null);
    }

    private async Task<JsonNode> GetScheduleAsync(string scheduleId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scheduleId)) throw ApiException.BadRequest("scheduleId is required.");
        JsonNode schedule;
        try
        {
            schedule = await _stax.GetAsync($"invoice/schedule/{Uri.EscapeDataString(scheduleId)}", ct);
        }
        catch (StaxException ex) when (ex.StaxStatus == 404 || ex.Message.Contains("not be found", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.NotFound("Invoice schedule not found.");
        }
        if (!string.IsNullOrEmpty(schedule.Str("deleted_at"))) throw ApiException.NotFound("Invoice schedule not found.");
        return schedule;
    }

    private static InvoiceScheduleDto ToDto(JsonNode s, string? company) => new()
    {
        ScheduleId = s.Str("id") ?? string.Empty,
        CustomerId = s.Str("customer_id"),
        Email = s["customer"].Str("email"),
        Company = company ?? s["customer"].Str("company"),
        Amount = s.Dec("total"),
        Active = s.Bool("active"),
        Status = s.Str("status") ?? (s.Bool("active") ? "ACTIVE" : "PAUSED"),
        NextInvoiceAt = s.Time("next_run_at")
    };

    // ---------------------------------------------------------------- sellers

    public async Task<SellerDto> OnboardSellerAsync(CreateSellerRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Company)) throw ApiException.BadRequest("company is required.");
        if (string.IsNullOrWhiteSpace(r.ContactName)) throw ApiException.BadRequest("contactName is required.");
        var email = ValidEmail(r.ContactEmail, "contactEmail");

        var enrolled = await _stax.PostPlatformAsync("admin/enroll", new
        {
            skip_account_page = false,
            merchant = new { plan = "connect-a", company_name = r.Company.Trim(), contact_email = email },
            user = new { email, name = r.ContactName.Trim(), password = RandomPassword(), password_confirmation = (string?)null },
            registration = new
            {
                pricing_plan = "test-pricing",
                business_legal_name = r.Company.Trim(),
                business_email = email,
                email,
                first_name = r.ContactName.Trim()
            }
        }.WithConfirmedPassword(), ct);

        var merchant = enrolled["merchant"];
        return new SellerDto
        {
            MerchantId = merchant.Str("id") ?? throw new ApiException(502, "Stax did not return a merchant id."),
            Status = merchant.Str("status"),
            Company = merchant.Str("company_name") ?? r.Company.Trim(),
            ContactEmail = email
        };
    }

    private static string ValidEmail(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw ApiException.BadRequest($"{field} is required.");
        try
        {
            var addr = new MailAddress(value.Trim());
            if (addr.Address != value.Trim()) throw new FormatException();
            return value.Trim();
        }
        catch (FormatException)
        {
            throw ApiException.BadRequest($"{field} is not a valid email address.");
        }
    }

    private static string RandomPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return "Aa1!" + Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
    }
}

internal static class EnrollBodyExtensions
{
    /// <summary>The enrollment API wants the new user's password repeated as password_confirmation.</summary>
    public static object WithConfirmedPassword(this object body)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToNode(body)!.AsObject();
        var user = json["user"]!.AsObject();
        user["password_confirmation"] = user["password"]!.GetValue<string>();
        return json;
    }
}
