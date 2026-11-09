using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class CreateInvoiceScheduleRequest : BaseRequest
{
    public string? CustomerEmail { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    /// <summary>First invoice day (yyyy-MM-dd). Today when omitted.</summary>
    public DateOnly? StartDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset? NextInvoiceAt { get; set; }
}

public class CreateSellerRequest : BaseRequest
{
    public string? Company { get; set; }
    /// <summary>Either an object {name, email, phone} or a plain string (email or name).</summary>
    public JsonElement? Contact { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

public class SellerDto
{
    public string SellerId { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Status { get; set; }
    public string? MerchantNumber { get; set; }
}

/// <summary>Staff features: monthly invoice schedules and marketplace seller onboarding.</summary>
public class BackOffice
{
    private const string INVOICE_URL = "https://app.staxpayments.com/#/bill/";

    private readonly StaxClient _stax;
    private readonly ShopperPayments _shoppers;

    public BackOffice(StaxClient stax, ShopperPayments shoppers)
    {
        _stax = stax;
        _shoppers = shoppers;
    }

    // ---------- invoice schedules ----------

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest request, CancellationToken ct)
    {
        var email = ShopperPayments.NormalizeEmail(request.CustomerEmail ?? "");
        if (!ShopperPayments.LooksLikeEmail(email)) throw ApiException.BadRequest("customerEmail must be a valid email address.");
        if (string.IsNullOrWhiteSpace(request.Company)) throw ApiException.BadRequest("company is required.");
        if (request.Amount <= 0 || request.Amount != Math.Round(request.Amount, 2)) throw ApiException.BadRequest("amount must be positive with at most two decimals.");

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        DateTimeOffset first;
        var day = request.StartDate ?? today;
        if (day < today) throw ApiException.BadRequest("startDate cannot be in the past.");
        if (day == today)
        {
            // Stax needs a start that is still in the future; "today" means as soon as possible today.
            first = now.AddMinutes(1);
            var endOfToday = new DateTimeOffset(today.Year, today.Month, today.Day, 23, 59, 59, TimeSpan.Zero);
            if (first > endOfToday) first = endOfToday;
        }
        else
        {
            first = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, TimeSpan.Zero);
        }

        var customerId = await _shoppers.GetOrCreateCustomerIdAsync(email, request.Company.Trim(), ct);
        var body = new JsonObject
        {
            ["url"] = INVOICE_URL,
            ["total"] = request.Amount,
            ["customer_id"] = customerId,
            ["rule"] = BuildRule(first, first.Day),
            ["email_notification"] = true,
            ["meta"] = new JsonObject
            {
                ["subtotal"] = request.Amount,
                ["lineItems"] = new JsonArray(new JsonObject { ["item"] = "Monthly invoice", ["quantity"] = 1, ["price"] = request.Amount })
            }
        };
        var created = await _stax.PostAsync("invoice/schedule", body, ct);
        var dto = ToSchedule(created!);
        dto.CustomerEmail = email;
        dto.Company = request.Company.Trim();
        dto.Amount = request.Amount;
        return dto;
    }

    /// <summary>Monthly rule; days 29-31 fall back to the last day of shorter months instead of skipping them.</summary>
    internal static string BuildRule(DateTimeOffset first, int dayOfMonth)
    {
        var rule = $"DTSTART={first.UtcDateTime:yyyyMMdd'T'HHmmss'Z'};FREQ=MONTHLY";
        if (dayOfMonth > 28) rule += $";BYMONTHDAY={string.Join(",", Enumerable.Range(28, dayOfMonth - 27))};BYSETPOS=-1";
        return rule;
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string scheduleId, CancellationToken ct)
    {
        await GetScheduleAsync(scheduleId, ct);
        var updated = await _stax.PutAsync($"invoice/schedule/{Uri.EscapeDataString(scheduleId)}", new JsonObject { ["active"] = false }, ct);
        return ToSchedule(updated!);
    }

    public async Task<InvoiceScheduleDto> ResumeAsync(string scheduleId, CancellationToken ct)
    {
        var schedule = await GetScheduleAsync(scheduleId, ct);
        var path = $"invoice/schedule/{Uri.EscapeDataString(scheduleId)}";
        if (schedule.Bool("active")) return ToSchedule(schedule);

        var rule = schedule.Str("rule") ?? "";
        var m = Regex.Match(rule, @"DTSTART=(\d{8}T\d{6}Z)");
        if (!m.Success) throw ApiException.Unprocessable("The schedule has no start date to resume from.");
        var threshold = DateTimeOffset.UtcNow.AddMinutes(2);
        var body = new JsonObject { ["active"] = true };
        var start = DateTimeOffset.ParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        if (start <= threshold)
        {
            // The start has passed: resume with the next monthly date, skipping what was missed while paused.
            var dm = Regex.Match(rule, @"BYMONTHDAY=([\d,]+)");
            var targetDay = dm.Success ? dm.Groups[1].Value.Split(',').Select(int.Parse).Max() : start.Day;
            var next = NextMonthly(start.TimeOfDay, targetDay, threshold);
            body["rule"] = Regex.Replace(rule, @"DTSTART=\d{8}T\d{6}Z", $"DTSTART={next.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
        }
        var updated = await _stax.PutAsync(path, body, ct);
        return ToSchedule(updated!);
    }

    internal static DateTimeOffset NextMonthly(TimeSpan timeOfDay, int targetDay, DateTimeOffset after)
    {
        var month = new DateTime(after.Year, after.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        while (true)
        {
            var day = Math.Min(targetDay, DateTime.DaysInMonth(month.Year, month.Month));
            var candidate = new DateTimeOffset(month.Year, month.Month, day, 0, 0, 0, TimeSpan.Zero) + timeOfDay;
            if (candidate > after) return candidate;
            month = month.AddMonths(1);
        }
    }

    private async Task<JsonNode> GetScheduleAsync(string scheduleId, CancellationToken ct)
    {
        try
        {
            var s = await _stax.GetAsync($"invoice/schedule/{Uri.EscapeDataString(scheduleId)}", ct);
            if (s == null || !string.IsNullOrEmpty(s.Str("deleted_at"))) throw ApiException.NotFound("Invoice schedule not found.");
            return s;
        }
        catch (StaxApiException ex) when (ex.StaxStatus is 400 or 404 or 422)
        {
            throw ApiException.NotFound("Invoice schedule not found.");
        }
    }

    private static InvoiceScheduleDto ToSchedule(JsonNode s) => new()
    {
        ScheduleId = s.Str("id") ?? string.Empty,
        Amount = s.Dec("total"),
        Active = s.Bool("active"),
        NextInvoiceAt = s.Date("next_run_at")
    };

    // ---------- sellers ----------

    public async Task<SellerDto> CreateSellerAsync(CreateSellerRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Company)) throw ApiException.BadRequest("company is required.");
        string? name = request.ContactName, email = request.ContactEmail, phone = request.ContactPhone;
        if (request.Contact is { } c)
        {
            if (c.ValueKind == JsonValueKind.Object)
            {
                name ??= Prop(c, "name");
                email ??= Prop(c, "email");
                phone ??= Prop(c, "phone");
            }
            else if (c.ValueKind == JsonValueKind.String)
            {
                var s = c.GetString();
                if (s != null && s.Contains('@')) email ??= s; else name ??= s;
            }
        }
        if (!ShopperPayments.LooksLikeEmail(email)) throw ApiException.BadRequest("A contact email is required (contact.email).");
        if (!_stax.HasPlatformKey)
            throw ApiException.Unprocessable("Seller onboarding is not available: the shop has no Stax platform key configured (Stax:PlatformApiKey).");

        var body = new JsonObject
        {
            ["company_name"] = request.Company.Trim(),
            ["contact_email"] = email!.Trim()
        };
        if (!string.IsNullOrWhiteSpace(name)) body["contact_name"] = name.Trim();
        if (!string.IsNullOrWhiteSpace(phone)) body["contact_phone"] = phone.Trim();
        var merchant = await _stax.PlatformPostAsync("merchant", body, ct);
        return new SellerDto
        {
            SellerId = merchant.Str("id") ?? string.Empty,
            Company = merchant.Str("company_name"),
            Status = merchant.Str("status"),
            MerchantNumber = merchant.Str("mid")
        };
    }

    private static string? Prop(JsonElement o, string name)
    {
        foreach (var p in o.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        return null;
    }
}

/// <summary>Closing a shopper account: erase their data at Stax and in the shop.</summary>
public class AccountCloser
{
    private readonly StaxClient _stax;
    private readonly ShopperPayments _shoppers;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public AccountCloser(StaxClient stax, ShopperPayments shoppers, CatalogContext db, UserManager<ApplicationUser> users)
    {
        _stax = stax;
        _shoppers = shoppers;
        _db = db;
        _users = users;
    }

    public async Task CloseAsync(string email, CancellationToken ct)
    {
        // Stax first: if it fails nothing local is lost and the shopper can simply retry.
        foreach (var customer in await _shoppers.FindCustomersAsync(email, ct))
        {
            var customerId = customer.Str("id")!;
            var scrub = new JsonObject { ["person_name"] = "Erased" };
            var active = await _stax.GetAllAsync($"payment-method?customer_id={Uri.EscapeDataString(customerId)}", ct);
            var removed = await _stax.GetAllAsync($"payment-method?customer_id={Uri.EscapeDataString(customerId)}&status=deleted", ct);
            foreach (var pm in active.Concat(removed))
            {
                var id = Uri.EscapeDataString(pm.Str("id")!);
                try { await _stax.PutAsync($"payment-method/{id}", scrub.DeepClone(), ct); } catch (StaxApiException) { }
                if (string.IsNullOrEmpty(pm.Str("deleted_at")))
                    await _stax.DeleteAsync($"payment-method/{id}", ct);
            }

            foreach (var s in await _stax.GetAllAsync($"invoice/schedule?customer_id={Uri.EscapeDataString(customerId)}", ct))
            {
                if (s.Str("customer_id") == customerId && string.IsNullOrEmpty(s.Str("deleted_at")))
                    await _stax.DeleteAsync($"invoice/schedule/{Uri.EscapeDataString(s.Str("id")!)}", ct);
            }

            await _stax.PutAsync($"customer/{Uri.EscapeDataString(customerId)}", new JsonObject
            {
                ["firstname"] = "Erased",
                ["lastname"] = "Erased",
                ["company"] = "",
                ["email"] = "",
                ["phone"] = "",
                ["reference"] = "",
                ["address_1"] = "",
                ["address_2"] = "",
                ["address_city"] = "",
                ["address_state"] = "",
                ["address_zip"] = "",
                ["address_country"] = "",
                ["notes"] = ""
            }, ct);
            await _stax.DeleteAsync($"customer/{Uri.EscapeDataString(customerId)}", ct);
        }

        // The shop. Orders and payment records are kept only anonymised: money may still be in flight.
        var orders = await _db.Orders.Where(o => o.BuyerId == email).ToListAsync(ct);
        foreach (var order in orders) order.Anonymize("erased-" + Guid.NewGuid().ToString("N"));
        var baskets = await _db.Baskets.Where(b => b.BuyerId == email).ToListAsync(ct);
        _db.Baskets.RemoveRange(baskets);
        await _db.SaveChangesAsync(ct);

        var user = await _users.FindByNameAsync(email) ?? await _users.FindByEmailAsync(email);
        if (user != null) await _users.DeleteAsync(user);
    }
}
