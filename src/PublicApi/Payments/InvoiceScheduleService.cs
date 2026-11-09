using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class InvoiceScheduleService
{
    // Stax only accepts a start strictly in the future, so "today" means "a moment from now".
    private static readonly TimeSpan IMMEDIATE_DELAY = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan FIRST_INVOICE_TIME = new(12, 0, 0);

    private readonly StaxClient _stax;

    public InvoiceScheduleService(StaxClient stax)
    {
        _stax = stax;
    }

    public async Task<object> CreateAsync(Body body, CancellationToken ct = default)
    {
        var email = body.Str("email", "customerEmail");
        var company = body.Str("company", "companyName", "customerCompany");
        var amount = body.Decimal("amount", "total");
        if (email == null || !email.Contains('@') || email.Length > 256) throw ApiException.BadRequest("A valid customer email is required.");
        if (company == null) throw ApiException.BadRequest("company is required.");
        if (amount == null || amount <= 0 || decimal.Round(amount.Value, 2) != amount)
            throw ApiException.BadRequest("amount must be a positive amount with at most two decimals.");

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        DateTimeOffset start;
        var rawDate = body.Str("firstInvoiceDate", "startDate", "firstInvoiceOn", "date");
        if (rawDate == null)
        {
            start = now + IMMEDIATE_DELAY;
        }
        else
        {
            if (!DateOnly.TryParseExact(rawDate[..Math.Min(10, rawDate.Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                throw ApiException.BadRequest("firstInvoiceDate must be a date like 2026-12-01.");
            if (day < today) throw ApiException.BadRequest("The first invoice cannot be in the past.");
            start = day == today
                ? now + IMMEDIATE_DELAY
                : new DateTimeOffset(day.ToDateTime(TimeOnly.FromTimeSpan(FIRST_INVOICE_TIME)), TimeSpan.Zero);
        }

        var customerId = await FindOrCreateCustomerAsync(email, company, ct);
        var rule = BuildRule(start);
        var created = await _stax.PostAsync("invoice/schedule", new
        {
            url = "https://omni.fattmerchant.com/#/bill/",
            total = amount,
            rule,
            customer_id = customerId,
            email_notification = true,
            meta = new { subtotal = amount, reference = "eShopOnWeb monthly invoice" }
        }, ct);
        return Map(created, company);
    }

    public async Task<object> PauseAsync(string scheduleId, CancellationToken ct = default)
    {
        var current = await GetAsync(scheduleId, ct);
        if (!IsActive(current)) return Map(current, null);
        var updated = await _stax.PutAsync($"invoice/schedule/{scheduleId}", new { active = false }, ct);
        return Map(updated, null);
    }

    public async Task<object> ResumeAsync(string scheduleId, CancellationToken ct = default)
    {
        var current = await GetAsync(scheduleId, ct);
        if (IsActive(current)) throw ApiException.Conflict("The schedule is not paused.");

        // Carry on from the next monthly occurrence rather than back-billing the paused months.
        var rule = current["rule"]?.GetValue<string>() ?? throw ApiException.Unprocessable("The schedule has no recurrence rule.");
        var m = Regex.Match(rule, @"DTSTART=(\d{8}T\d{6}Z)");
        if (!m.Success || !DateTimeOffset.TryParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var anchor))
            throw ApiException.Unprocessable("The schedule's recurrence rule has no start date.");

        var earliest = DateTimeOffset.UtcNow + IMMEDIATE_DELAY;
        var next = anchor;
        var months = 0;
        var neverInvoiced = (current["count"] is JsonValue cv && cv.TryGetValue<int>(out var cnt) ? cnt : 0) == 0
                            && current["last_run_at"] == null;
        // Paused before the first invoice went out: that invoice is still owed, so start it now.
        if (neverInvoiced && next <= earliest) next = earliest.AddSeconds(1);
        while (next <= earliest)
        {
            months++;
            next = AddMonthsClamped(anchor, months);
        }
        var newRule = Regex.Replace(rule, @"DTSTART=\d{8}T\d{6}Z", "DTSTART=" + next.ToString("yyyyMMdd'T'HHmmss'Z'"));
        var updated = await _stax.PutAsync($"invoice/schedule/{scheduleId}", new { active = true, rule = newRule }, ct);
        return Map(updated, null);
    }

    private async Task<JsonNode> GetAsync(string scheduleId, CancellationToken ct)
    {
        if (!PaymentMethodService.IsSafeId(scheduleId)) throw ApiException.NotFound("No such invoice schedule.");
        try
        {
            var all = await _stax.GetAllPagesAsync("invoice/schedule", null, ct);
            var found = all.FirstOrDefault(s => s["id"]?.GetValue<string>() == scheduleId);
            if (found != null) return found;
        }
        catch (StaxException ex) when (ex.UpstreamStatus is 404 or 405) { }
        throw ApiException.NotFound("No such invoice schedule.");
    }

    private static bool IsActive(JsonNode schedule) =>
        schedule["active"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b
        : schedule["active"] is JsonValue n && n.TryGetValue<int>(out var i) && i != 0;

    /// <summary>
    /// Monthly on the start's day of month. Days 29-31 fall back to the month's last day in
    /// shorter months instead of skipping them.
    /// </summary>
    private static string BuildRule(DateTimeOffset start)
    {
        var rule = "DTSTART=" + start.ToString("yyyyMMdd'T'HHmmss'Z'") + ";FREQ=MONTHLY";
        if (start.Day > 28)
            rule += ";BYMONTHDAY=" + string.Join(",", Enumerable.Range(28, start.Day - 27)) + ";BYSETPOS=-1";
        return rule;
    }

    private static DateTimeOffset AddMonthsClamped(DateTimeOffset anchor, int months)
    {
        var first = new DateTimeOffset(anchor.Year, anchor.Month, 1, anchor.Hour, anchor.Minute, anchor.Second, TimeSpan.Zero).AddMonths(months);
        var day = Math.Min(anchor.Day, DateTime.DaysInMonth(first.Year, first.Month));
        return new DateTimeOffset(first.Year, first.Month, day, anchor.Hour, anchor.Minute, anchor.Second, TimeSpan.Zero);
    }

    private async Task<string> FindOrCreateCustomerAsync(string email, string company, CancellationToken ct)
    {
        var rows = await _stax.GetAllPagesAsync("customer", new Dictionary<string, string?> { ["email"] = email }, ct);
        var match = rows.FirstOrDefault(c =>
            c["deleted_at"] == null &&
            string.Equals(c["email"]?.GetValue<string>()?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c["company"]?.GetValue<string>()?.Trim(), company, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match["id"]!.GetValue<string>();

        var created = await _stax.PostAsync("customer", new
        {
            firstname = company,
            lastname = "",
            company,
            email = email.Trim(),
            reference = "eshop-business-customer"
        }, ct);
        return created["id"]?.GetValue<string>() ?? throw new ApiException(502, "Stax did not return a customer id.");
    }

    private static object Map(JsonNode s, string? company) => new
    {
        id = s["id"]?.GetValue<string>(),
        scheduleId = s["id"]?.GetValue<string>(),
        status = IsActive(s) ? "active" : "paused",
        customerId = s["customer_id"]?.GetValue<string>(),
        company = company ?? s["customer"]?["company"]?.GetValue<string>(),
        email = s["customer"]?["email"]?.GetValue<string>(),
        amount = s["total"] is JsonValue t && t.TryGetValue<decimal>(out var d) ? d : (decimal?)null,
        rule = s["rule"]?.GetValue<string>(),
        nextInvoiceAt = ShopperService.ParseStaxTime(s["next_run_at"]),
        upcoming = (s["future_occurrences"] as JsonArray)?.Take(3).Select(x => x?.GetValue<string>()).ToArray()
    };
}
