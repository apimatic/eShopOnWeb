using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.Team;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record CreateInvoiceScheduleRequest(string? Email, string? Company, decimal? Amount, string? StartDate);

public record InvoiceScheduleDto(string Id, string CustomerEmail, string? Company, decimal Amount, string Interval, DateTime? FirstInvoiceDate,
    string Status, bool Active, DateTime? NextInvoiceAt);

public record SellerContact(string? Name, string? Email, string? Phone);

public record OnboardSellerRequest(string? Company, SellerContact? Contact, string? ContactName, string? ContactEmail, string? ContactPhone);

public record SellerDto(string Id, string Company, string? Status, string? ContactName, string? ContactEmail, string? ContactPhone);

public sealed class InvoiceAndSellerService
{
    private readonly StaxGateway _stax;
    private readonly ShopperService _shoppers;
    private readonly BaseUrlConfiguration _urls;

    public InvoiceAndSellerService(StaxGateway stax, ShopperService shoppers, IOptions<BaseUrlConfiguration> urls)
    {
        _stax = stax;
        _shoppers = shoppers;
        _urls = urls.Value;
    }

    private static Guid G(string id) => Guid.TryParse(id, out var g) ? g : throw PaymentException.NotFound("No such invoice schedule.");

    public static bool IsEmail(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        try { return new MailAddress(s).Address == s.Trim(); }
        catch (FormatException) { return false; }
    }

    // ------------------------------------------------------------------ monthly invoices

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest req)
    {
        if (!IsEmail(req.Email)) throw PaymentException.BadRequest("A valid customer email is required.");
        if (string.IsNullOrWhiteSpace(req.Company)) throw PaymentException.BadRequest("The customer's company is required.");
        if (req.Amount is not > 0m || req.Amount != Math.Round(req.Amount.Value, 2)) throw PaymentException.BadRequest("amount must be a positive amount in dollars and cents.");

        var today = DateTime.UtcNow.Date;
        var start = today;
        if (!string.IsNullOrWhiteSpace(req.StartDate))
        {
            if (!DateTime.TryParseExact(req.StartDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                throw PaymentException.BadRequest("startDate must be a date formatted yyyy-MM-dd.");
            if (start < today) throw PaymentException.BadRequest("startDate can't be in the past.");
        }

        var email = ShopperService.Normalize(req.Email!);
        var company = req.Company!.Trim();
        var customerId = await _shoppers.EnsureCustomerAsync(email, company);

        // Same day every month; on the 29th-31st, months that are too short get their last day.
        // Today means right now (Stax refuses a start earlier than the moment of the request); a later day starts at its midnight, UTC.
        var dtStart = start == today ? DateTime.UtcNow : start;
        var stamp = dtStart.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var rule = $"FREQ=MONTHLY;DTSTART={stamp}";
        if (start.Day > 28)
        {
            var days = string.Join(",", Enumerable.Range(start.Day, 32 - start.Day));
            rule = $"FREQ=MONTHLY;BYMONTHDAY=-1,{days};BYSETPOS=1;DTSTART={stamp}";
        }

        var created = Js.El(await _stax.Call(c => c.InvoiceSchedule.PostInvoiceSchedule(new PostInvoiceScheduleRequest
        {
            Body = new
            {
                customer_id = customerId,
                total = req.Amount!.Value,
                url = (_urls.WebBase ?? "https://localhost").TrimEnd('/') + "/",
                rule,
                meta = new { description = $"Monthly invoice for {company}", subtotal = req.Amount.Value, source = "eShopOnWeb" }
            }
        })));
        return ToDto(created, email, company, req.Amount.Value, start);
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string id) => await SetActiveAsync(id, false);

    public async Task<InvoiceScheduleDto> ResumeAsync(string id) => await SetActiveAsync(id, true);

    private async Task<InvoiceScheduleDto> SetActiveAsync(string id, bool active)
    {
        var gid = G(id);
        var gate = ShopperService.LockFor("schedule:" + gid);
        await gate.WaitAsync();
        try
        {
            var current = Js.El(await _stax.Call(c => c.InvoiceSchedule.GetInvoiceSchedule(new GetInvoiceScheduleRequest { Id = gid })));
            if (current.Str("deleted_at") != null) throw PaymentException.NotFound("No such invoice schedule.");
            var isActive = current.Bool("active") == true;
            if (isActive == active)
                throw PaymentException.Conflict(active ? "The schedule is already running." : "The schedule is already paused.");
            var updated = Js.El(await _stax.Call(c => c.InvoiceSchedule.PutInvoiceSchedule(new PutInvoiceScheduleRequest
            {
                Id = gid,
                Body = new { active }
            })));
            return ToDto(updated, updated.Prop("customer")?.Str("email"), updated.Prop("customer")?.Str("company"), updated.Dec("total") ?? 0m, null);
        }
        finally
        {
            gate.Release();
        }
    }

    private static InvoiceScheduleDto ToDto(JsonElement s, string? email, string? company, decimal amount, DateTime? start)
    {
        var customer = s.Prop("customer");
        return new InvoiceScheduleDto(
            s.Str("id") ?? "",
            email ?? customer?.Str("email") ?? "",
            company ?? customer?.Str("company"),
            s.Dec("total") ?? amount,
            "monthly",
            start ?? ParseStart(s.Str("rule")),
            (s.Str("status") ?? (s.Bool("active") == true ? "ACTIVE" : "PAUSED")).ToLowerInvariant().Replace(' ', '-'),
            s.Bool("active") == true,
            s.Time("next_run_at"));
    }

    private static DateTime? ParseStart(string? rule)
    {
        var i = rule?.IndexOf("DTSTART=", StringComparison.Ordinal) ?? -1;
        return i >= 0 && DateTime.TryParseExact(rule!.Substring(i + 8, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // ------------------------------------------------------------------ sellers

    public async Task<SellerDto> OnboardSellerAsync(OnboardSellerRequest req)
    {
        var company = req.Company?.Trim();
        var name = (req.Contact?.Name ?? req.ContactName)?.Trim();
        var email = (req.Contact?.Email ?? req.ContactEmail)?.Trim();
        var phone = (req.Contact?.Phone ?? req.ContactPhone)?.Trim();
        if (string.IsNullOrEmpty(company)) throw PaymentException.BadRequest("The seller's company is required.");
        if (string.IsNullOrEmpty(name)) throw PaymentException.BadRequest("The seller's contact name is required.");
        if (!IsEmail(email)) throw PaymentException.BadRequest("A valid contact email is required.");

        var created = Js.El(await _stax.CallPlatform(c => c.Team.PostMerchant(new PostMerchantRequest
        {
            Body = new
            {
                company_name = company,
                contact_name = name,
                contact_email = email,
                contact_phone = phone ?? ""
            }
        })));
        return new SellerDto(created.Str("id") ?? "", created.Str("company_name") ?? company, created.Str("status"),
            created.Str("contact_name") ?? name, created.Str("contact_email") ?? email, created.Str("contact_phone") ?? phone);
    }
}
