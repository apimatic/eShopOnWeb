using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public record CreateInvoiceScheduleRequest(string? Email, string? Company, decimal? Amount, string? FirstInvoiceDate);

public record InvoiceScheduleDto(
    string ScheduleId,
    string? CustomerId,
    string? Email,
    string? Company,
    decimal Amount,
    bool Active,
    string Status,
    string? NextRunAt,
    string? Rule);

public class InvoiceService
{
    private const string Fmt = "yyyyMMdd'T'HHmmss'Z'";
    private readonly StaxClient _stax;

    public InvoiceService(StaxClient stax) => _stax = stax;

    public static bool IsEmail(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 254) return false;
        try { return new MailAddress(s).Address == s.Trim(); } catch (FormatException) { return false; }
    }

    private static string RuleFor(DateTime dtstart, int day)
    {
        var rule = $"DTSTART={dtstart.ToString(Fmt, CultureInfo.InvariantCulture)};FREQ=MONTHLY";
        // Days 29-31 do not exist in every month: fall back to the month's last day instead of skipping it.
        if (day > 28) rule += $";BYMONTHDAY={string.Join(',', Enumerable.Range(28, day - 27))};BYSETPOS=-1";
        return rule;
    }

    public async Task<InvoiceScheduleDto> CreateAsync(CreateInvoiceScheduleRequest r, CancellationToken ct = default)
    {
        if (!IsEmail(r.Email)) throw ApiException.BadRequest("A valid customer email is required.");
        if (string.IsNullOrWhiteSpace(r.Company)) throw ApiException.BadRequest("company is required.");
        if (r.Amount is null or <= 0 || ShopOrderService.Round(r.Amount.Value) != r.Amount) throw ApiException.BadRequest("amount must be a positive amount with at most 2 decimals.");

        var now = DateTime.UtcNow;
        var first = now.Date;
        if (!string.IsNullOrWhiteSpace(r.FirstInvoiceDate))
        {
            if (!DateTime.TryParseExact(r.FirstInvoiceDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
                throw ApiException.BadRequest("firstInvoiceDate must be a date like 2026-12-01.");
            if (first < now.Date) throw ApiException.BadRequest("firstInvoiceDate cannot be in the past.");
        }
        // Stax only accepts a start moment in the future: "today" means a couple of minutes from now.
        var dtstart = first == now.Date ? now.AddMinutes(2) : first.AddHours(12);
        dtstart = new DateTime(dtstart.Year, dtstart.Month, dtstart.Day, dtstart.Hour, dtstart.Minute, dtstart.Second, DateTimeKind.Utc);

        var email = r.Email!.Trim();
        var company = r.Company.Trim();
        var matches = await _stax.ListAllAsync("/customer?email=" + Uri.EscapeDataString(email), ct);
        var customer = matches.FirstOrDefault(c =>
            string.Equals(StaxClient.Str(c, "email"), email, StringComparison.OrdinalIgnoreCase)
            && string.Equals(StaxClient.Str(c, "company"), company, StringComparison.OrdinalIgnoreCase)
            && StaxClient.Str(c, "deleted_at") == null);
        var customerId = customer != null
            ? StaxClient.Str(customer, "id")
            : StaxClient.Str(await _stax.PostAsync("/customer", new { company, email }, ct), "id");

        var schedule = await _stax.PostAsync("/invoice/schedule", new
        {
            url = _stax.InvoiceUrl,
            total = r.Amount,
            customer_id = customerId,
            rule = RuleFor(dtstart, first.Day),
            email_notification = true,
            meta = new { subtotal = r.Amount, tax = 0, lineItems = new[] { new { item = "Monthly invoice", quantity = 1, price = r.Amount } } },
        }, ct);
        return ToDto(schedule, email, company);
    }

    private async Task<JsonNode> GetScheduleAsync(string id, CancellationToken ct)
    {
        try { return await _stax.GetAsync("/invoice/schedule/" + Uri.EscapeDataString(id), ct); }
        catch (StaxException ex) when (ex.StaxStatus is 400 or 404 or 422) { throw ApiException.NotFound("Invoice schedule not found."); }
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string id, CancellationToken ct = default)
    {
        var s = await GetScheduleAsync(id, ct);
        if (StaxClient.Bool(s, "active"))
            s = await _stax.PutAsync("/invoice/schedule/" + Uri.EscapeDataString(id), new { active = false }, ct);
        return ToDto(s);
    }

    public async Task<InvoiceScheduleDto> ResumeAsync(string id, CancellationToken ct = default)
    {
        var s = await GetScheduleAsync(id, ct);
        if (StaxClient.Bool(s, "active")) return ToDto(s);

        // A resumed schedule must start in the future, so move the start to the next billing day.
        var rule = StaxClient.Str(s, "rule") ?? "";
        var dt = Regex.Match(rule, @"DTSTART=(\d{8}T\d{6}Z)");
        if (!dt.Success || !DateTime.TryParseExact(dt.Groups[1].Value, Fmt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start))
            throw ApiException.Unprocessable("The schedule's recurrence rule could not be understood.");
        var day = start.Day;
        var by = Regex.Match(rule, @"BYMONTHDAY=([\d,]+)");
        if (by.Success) day = by.Groups[1].Value.Split(',').Select(int.Parse).Max();

        var floor = DateTime.UtcNow.AddMinutes(2);
        var next = start;
        var neverRan = !(s["count"] is JsonValue c && c.TryGetValue<int>(out var runs) && runs > 0) && StaxClient.Str(s, "last_run_at") == null;
        if (start <= floor && neverRan && start.Date == DateTime.UtcNow.Date)
        {
            // Paused before its first invoice on the day it was due: bill as soon as possible.
            next = new DateTime(floor.Year, floor.Month, floor.Day, floor.Hour, floor.Minute, floor.Second, DateTimeKind.Utc);
        }
        else if (start <= floor)
        {
            var month = new DateTime(floor.Year, floor.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (true)
            {
                var d = Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month));
                next = new DateTime(month.Year, month.Month, d, start.Hour, start.Minute, start.Second, DateTimeKind.Utc);
                if (next > floor) break;
                month = month.AddMonths(1);
            }
        }
        var newRule = Regex.Replace(rule, @"DTSTART=\d{8}T\d{6}Z", "DTSTART=" + next.ToString(Fmt, CultureInfo.InvariantCulture));
        s = await _stax.PutAsync("/invoice/schedule/" + Uri.EscapeDataString(id), new { active = true, rule = newRule }, ct);
        return ToDto(s);
    }

    private static InvoiceScheduleDto ToDto(JsonNode s, string? email = null, string? company = null)
    {
        var customer = s["customer"];
        var active = StaxClient.Bool(s, "active");
        return new InvoiceScheduleDto(
            StaxClient.Str(s, "id")!,
            StaxClient.Str(s, "customer_id"),
            email ?? StaxClient.Str(customer, "email"),
            company ?? StaxClient.Str(customer, "company"),
            StaxClient.Dec(s, "total"),
            active,
            active ? "active" : "paused",
            StaxClient.Str(s, "next_run_at"),
            StaxClient.Str(s, "rule"));
    }
}
