using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class CreateInvoiceScheduleRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }

    /// <summary>First invoice day (yyyy-MM-dd). Today when omitted.</summary>
    public string? FirstInvoiceDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? NextInvoiceAt { get; set; }
    public string? Rule { get; set; }
}

public class InvoiceScheduleService
{
    private readonly StaxClient _stax;
    private readonly IConfiguration _config;

    public InvoiceScheduleService(StaxClient stax, IConfiguration config)
    {
        _stax = stax;
        _config = config;
    }

    public async Task<InvoiceScheduleDto> CreateAsync(CreateInvoiceScheduleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !IsEmail(request.Email))
        {
            throw ApiException.BadRequest("A valid customer email is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Company))
        {
            throw ApiException.BadRequest("The customer's company is required.");
        }
        if (request.Amount <= 0m || decimal.Round(request.Amount, 2) != request.Amount)
        {
            throw ApiException.BadRequest("amount must be a positive amount with at most two decimals.");
        }

        var now = DateTime.UtcNow;
        DateTime start;
        if (string.IsNullOrWhiteSpace(request.FirstInvoiceDate))
        {
            start = StartForToday(now);
        }
        else
        {
            if (!DateOnly.TryParseExact(request.FirstInvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                throw ApiException.BadRequest("firstInvoiceDate must look like 2026-12-31.");
            }
            if (day < DateOnly.FromDateTime(now))
            {
                throw ApiException.BadRequest("firstInvoiceDate can't be in the past.");
            }
            start = day == DateOnly.FromDateTime(now) ? StartForToday(now) : day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        }

        var customerId = await FindOrCreateBusinessCustomerAsync(request.Email.Trim(), request.Company.Trim(), ct);
        var webBase = (_config["baseUrls:webBase"] ?? "https://localhost").TrimEnd('/');
        var rule = $"DTSTART={start:yyyyMMdd'T'HHmmss'Z'};FREQ=MONTHLY";

        var schedule = await _stax.PostAsync("invoice/schedule", new JsonObject
        {
            ["url"] = $"{webBase}/invoice",
            ["total"] = request.Amount,
            ["customer_id"] = customerId,
            ["rule"] = rule,
            ["email_notification"] = true,
            ["meta"] = new JsonObject
            {
                ["subtotal"] = request.Amount,
                ["lineItems"] = new JsonArray(new JsonObject
                {
                    ["item"] = "Monthly invoice",
                    ["quantity"] = 1,
                    ["price"] = request.Amount
                })
            }
        }, ct);
        return ToDto(schedule, request.Amount);
    }

    /// <summary>Stax refuses a start in the past, so "today" means a moment from now (or the end of today if that is about to roll over).</summary>
    private static DateTime StartForToday(DateTime now)
    {
        var start = now.AddMinutes(1);
        return start.Date == now.Date ? start : now.AddSeconds(30);
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string scheduleId, CancellationToken ct)
    {
        await RequireAsync(scheduleId, ct);
        var updated = await _stax.PutAsync($"invoice/schedule/{Uri.EscapeDataString(scheduleId)}", new JsonObject { ["active"] = false }, ct);
        return ToDto(updated);
    }

    public async Task<InvoiceScheduleDto> ResumeAsync(string scheduleId, CancellationToken ct)
    {
        var schedule = await RequireAsync(scheduleId, ct);
        var body = new JsonObject { ["active"] = true };

        // Stax rejects a resume date in the past, so continue from the next occurrence still to come.
        var next = (schedule["future_occurrences"] as JsonArray)?
            .Select(o => o?.ToString())
            .Where(o => DateTime.TryParse(o, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) && d > DateTime.UtcNow.AddMinutes(1))
            .FirstOrDefault();
        if (next is not null) body["next_run_at"] = next;

        var updated = await _stax.PutAsync($"invoice/schedule/{Uri.EscapeDataString(scheduleId)}", body, ct);
        return ToDto(updated);
    }

    private async Task<JsonNode> RequireAsync(string scheduleId, CancellationToken ct)
    {
        try
        {
            return await _stax.GetAsync($"invoice/schedule/{Uri.EscapeDataString(scheduleId)}", ct);
        }
        catch (StaxException ex) when (ex.StaxStatusCode is 404 or 422 or 400)
        {
            throw ApiException.NotFound("Invoice schedule not found.");
        }
    }

    private async Task<string> FindOrCreateBusinessCustomerAsync(string email, string company, CancellationToken ct)
    {
        for (var page = 1; page <= 20; page++)
        {
            var found = await _stax.GetAsync($"customer?email={Uri.EscapeDataString(email)}&per_page=100&page={page}", ct);
            var match = found.Items().FirstOrDefault(c =>
                ShopperService.SameEmail(c.Str("email"), email)
                && string.Equals(c.Str("company")?.Trim(), company, StringComparison.OrdinalIgnoreCase)
                && c.Str("deleted_at") is null);
            if (match is not null) return match.Str("id")!;
            if (page >= (found["last_page"]?.GetValue<int>() ?? 1)) break;
        }

        var created = await _stax.PostAsync("customer", new JsonObject
        {
            ["firstname"] = company,
            ["lastname"] = "",
            ["company"] = company,
            ["email"] = email
        }, ct);
        return created.Str("id") ?? throw new StaxException(502, "Stax did not return a customer id.");
    }

    private static InvoiceScheduleDto ToDto(JsonNode s, decimal? amount = null)
    {
        var active = s.Bool("active");
        return new InvoiceScheduleDto
        {
            ScheduleId = s.Str("id") ?? string.Empty,
            CustomerId = s.Str("customer_id"),
            Amount = amount ?? s.Dec("total"),
            Active = active,
            Status = active ? "active" : "paused",
            NextInvoiceAt = s.Str("next_run_at"),
            Rule = s.Str("rule")
        };
    }

    private static bool IsEmail(string value)
    {
        try
        {
            return new MailAddress(value.Trim()).Address == value.Trim();
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
