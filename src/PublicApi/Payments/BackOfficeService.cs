using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateInvoiceScheduleRequest : BaseRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    /// <summary>First invoice day (yyyy-MM-dd). Today when omitted.</summary>
    public string? StartDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal? Amount { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset? NextInvoiceAt { get; set; }
    public string Rule { get; set; } = "";
}

public class SellerContactRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class CreateSellerRequest : BaseRequest
{
    public string? Company { get; set; }
    public SellerContactRequest? Contact { get; set; }
}

public class SellerDto
{
    public string MerchantId { get; set; } = "";
    public string? Company { get; set; }
    public string? Status { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
}

/// <summary>Staff-only Stax features: invoice schedules and seller (merchant) onboarding.</summary>
public class BackOfficeService
{
    // Stax links an invoice schedule to a hosted page; this is Stax's own invoice portal.
    private const string INVOICE_URL = "https://app.staxpayments.com/#/bill/";
    private static readonly Regex DtStart = new(@"DTSTART=(\d{8}T\d{6}Z)", RegexOptions.Compiled);

    private readonly StaxClient _stax;

    public BackOfficeService(StaxClient stax) => _stax = stax;

    private static string Fmt(DateTimeOffset d) => d.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    private static InvoiceScheduleDto ToDto(JsonNode s, string? email, string? company, decimal? amount) => new()
    {
        ScheduleId = s["id"]?.ToString() ?? "",
        CustomerId = s["customer_id"]?.ToString() ?? "",
        Email = email,
        Company = company,
        Amount = amount ?? (s["total"] is null ? null : s["total"]!.GetValue<decimal>()),
        Active = s["active"]?.GetValue<bool>() ?? false,
        NextInvoiceAt = StaxClient.ParseStaxTime(s["next_run_at"]),
        Rule = s["rule"]?.ToString() ?? ""
    };

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest r, CancellationToken ct)
    {
        if (!ShopperContext.IsValidEmail(r.Email)) throw new ApiRequestException(400, "A valid customer email is required.");
        if (string.IsNullOrWhiteSpace(r.Company)) throw new ApiRequestException(400, "company is required.");
        if (r.Amount <= 0 || r.Amount != Math.Round(r.Amount, 2)) throw new ApiRequestException(400, "amount must be a positive amount with at most two decimals.");

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        DateOnly start = today;
        if (!string.IsNullOrWhiteSpace(r.StartDate))
        {
            if (!DateOnly.TryParseExact(r.StartDate.Trim(), "yyyy-MM-dd", out start))
                throw new ApiRequestException(400, "startDate must look like yyyy-MM-dd.");
            if (start < today) throw new ApiRequestException(400, "startDate cannot be in the past.");
        }
        // Stax only accepts a start in the future: "today" means a moment from now.
        var dtstart = start == today ? now.AddMinutes(2) : new DateTimeOffset(start.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

        var email = ShopperContext.Normalize(r.Email!);
        var company = r.Company.Trim();
        var customers = await _stax.FindCustomersByEmailAsync(email, ct);
        var customer = customers.FirstOrDefault(c => c["deleted_at"] is null);
        string customerId;
        if (customer is null)
        {
            customerId = (await _stax.CreateCustomerAsync(email, "Accounts", "Payable", company, ct))["id"]!.ToString();
        }
        else
        {
            customerId = customer["id"]!.ToString();
            if (!string.Equals(customer["company"]?.ToString(), company, StringComparison.Ordinal))
                await _stax.UpdateCustomerAsync(customerId, new { company }, ct);
        }

        var schedule = await _stax.CreateInvoiceScheduleAsync(customerId, r.Amount, $"DTSTART={Fmt(dtstart)};FREQ=MONTHLY", INVOICE_URL, ct);
        return ToDto(schedule, email, company, r.Amount);
    }

    private async Task<JsonNode> GetScheduleAsync(string scheduleId, CancellationToken ct)
    {
        if (!Regex.IsMatch(scheduleId, "^[A-Za-z0-9-]{8,64}$")) throw new ApiRequestException(404, "Invoice schedule not found.");
        try { return await _stax.GetInvoiceScheduleAsync(scheduleId, ct); }
        catch (StaxException ex) when (ex.IsRejection) { throw new ApiRequestException(404, "Invoice schedule not found."); }
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string scheduleId, CancellationToken ct)
    {
        var s = await GetScheduleAsync(scheduleId, ct);
        if (s["active"]?.GetValue<bool>() == true)
            s = await _stax.UpdateInvoiceScheduleAsync(scheduleId, new { active = false }, ct);
        return ToDto(s, null, null, null);
    }

    public async Task<InvoiceScheduleDto> ResumeAsync(string scheduleId, CancellationToken ct)
    {
        var s = await GetScheduleAsync(scheduleId, ct);
        if (s["active"]?.GetValue<bool>() != true)
        {
            // A resumed schedule needs a future start. Keep the monthly day: use the next occurrence after now.
            var rule = s["rule"]?.ToString() ?? "";
            var m = DtStart.Match(rule);
            var origin = m.Success
                ? DateTimeOffset.ParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal)
                : DateTimeOffset.UtcNow;
            var floor = DateTimeOffset.UtcNow.AddMinutes(2);
            var next = origin;
            for (var k = 0; next <= floor && k < 100000; k++) next = origin.AddMonths(k);
            var newRule = m.Success ? DtStart.Replace(rule, $"DTSTART={Fmt(next)}") : $"DTSTART={Fmt(next)};FREQ=MONTHLY";
            s = await _stax.UpdateInvoiceScheduleAsync(scheduleId, new { active = true, rule = newRule }, ct);
        }
        return ToDto(s, null, null, null);
    }

    public async Task<SellerDto> OnboardSellerAsync(CreateSellerRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Company)) throw new ApiRequestException(400, "company is required.");
        var c = r.Contact;
        if (c is null || string.IsNullOrWhiteSpace(c.Name)) throw new ApiRequestException(400, "contact.name is required.");
        if (!ShopperContext.IsValidEmail(c.Email)) throw new ApiRequestException(400, "A valid contact.email is required.");
        if (!_stax.HasPlatformKey)
            throw new ApiRequestException(422, "Seller onboarding is not available: the shop has no Stax platform key configured.");

        var merchant = await _stax.CreateMerchantAsync(new
        {
            company_name = r.Company.Trim(),
            contact_name = c.Name.Trim(),
            contact_email = c.Email!.Trim(),
            contact_phone = c.Phone?.Trim() ?? ""
        }, ct);

        return new SellerDto
        {
            MerchantId = merchant["id"]?.ToString() ?? "",
            Company = merchant["company_name"]?.ToString() ?? r.Company.Trim(),
            Status = merchant["status"]?.ToString(),
            ContactName = merchant["contact_name"]?.ToString(),
            ContactEmail = merchant["contact_email"]?.ToString()
        };
    }
}
