using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Staff features: monthly invoice schedules and onboarding sellers.
/// </summary>
public class StaffBillingService
{
    private readonly StaxGateway _stax;
    private readonly BaseUrlConfiguration _urls;

    public StaffBillingService(StaxGateway stax, IOptions<BaseUrlConfiguration> urls)
    {
        _stax = stax;
        _urls = urls.Value;
    }

    // ---------- invoice schedules ----------

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest request)
    {
        var email = request.Email?.Trim();
        var company = request.Company?.Trim();
        if (string.IsNullOrEmpty(email) || !IsEmail(email))
        {
            throw new ApiProblemException(400, "A valid customer email is required.");
        }
        if (string.IsNullOrEmpty(company))
        {
            throw new ApiProblemException(400, "company is required.");
        }
        if (request.Amount is null || request.Amount <= 0 || request.Amount > 1_000_000m
            || decimal.Round(request.Amount.Value, 2) != request.Amount.Value)
        {
            throw new ApiProblemException(400, "amount must be greater than 0 (at most 2 decimals).");
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        DateOnly firstDay = today;
        if (!string.IsNullOrWhiteSpace(request.FirstInvoiceDate))
        {
            if (!DateOnly.TryParseExact(request.FirstInvoiceDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out firstDay))
            {
                throw new ApiProblemException(400, "firstInvoiceDate must look like yyyy-MM-dd.");
            }
            if (firstDay < today)
            {
                throw new ApiProblemException(400, "firstInvoiceDate cannot be in the past.");
            }
        }

        // First invoice: today means as soon as Stax allows (it only accepts a start in the future); a later day starts at noon UTC.
        DateTimeOffset start = firstDay == today
            ? new DateTimeOffset(now.UtcDateTime.AddMinutes(2).AddTicks(-(now.UtcDateTime.AddMinutes(2).Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero)
            : new DateTimeOffset(firstDay.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var rule = BuildMonthlyRule(start);

        var customerId = await _stax.FindOrCreateBusinessCustomerAsync(email, company);

        var amount = request.Amount.Value;
        var body = new Dictionary<string, object?>
        {
            ["customer_id"] = customerId,
            ["total"] = amount,
            ["url"] = string.IsNullOrWhiteSpace(_urls.WebBase) ? "https://localhost/" : _urls.WebBase,
            ["rule"] = rule,
            ["meta"] = new Dictionary<string, object?>
            {
                ["subtotal"] = amount,
                ["memo"] = $"Monthly invoice for {company}",
            },
        };
        var created = await _stax.CreateInvoiceScheduleAsync(body);
        var dto = ToDto(created, email, company, amount);
        dto.FirstInvoiceDate = DateOnly.FromDateTime(start.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return dto;
    }

    /// <summary>
    /// A monthly rule starting at <paramref name="start"/>. For days 29-31, months without that day use their last day
    /// instead of being skipped.
    /// </summary>
    private static string BuildMonthlyRule(DateTimeOffset start)
    {
        var dtstart = start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var day = start.UtcDateTime.Day;
        if (day <= 28)
        {
            return $"FREQ=MONTHLY;DTSTART={dtstart}";
        }
        var days = string.Join(",", Enumerable.Range(day, 32 - day).Append(-1).Distinct());
        return $"FREQ=MONTHLY;BYMONTHDAY={days};BYSETPOS=1;DTSTART={dtstart}";
    }

    public Task<InvoiceScheduleDto> PauseAsync(string scheduleId) => SetActiveAsync(scheduleId, false);

    public Task<InvoiceScheduleDto> ResumeAsync(string scheduleId) => SetActiveAsync(scheduleId, true);

    private async Task<InvoiceScheduleDto> SetActiveAsync(string scheduleId, bool active)
    {
        JsonElement current;
        try
        {
            current = await _stax.GetInvoiceScheduleAsync(scheduleId);
        }
        catch (ApiProblemException ex) when (ex.StatusCode == 422 && ex.Message.Contains("not be found"))
        {
            throw new ApiProblemException(404, "Invoice schedule not found.");
        }
        if (current.Str("deleted_at") is not null)
        {
            throw new ApiProblemException(404, "Invoice schedule not found.");
        }
        if (current.Bool("active") == active)
        {
            throw new ApiProblemException(409, active ? "The schedule is already active." : "The schedule is already paused.");
        }
        var updated = await _stax.SetInvoiceScheduleActiveAsync(scheduleId, active);
        return ToDto(updated, null, null, null);
    }

    private static InvoiceScheduleDto ToDto(JsonElement e, string? email, string? company, decimal? amount)
    {
        string? mail = email, comp = company;
        if (e.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            mail ??= c.Str("email");
            comp ??= c.Str("company");
        }
        return new InvoiceScheduleDto
        {
            ScheduleId = e.Str("id") ?? string.Empty,
            Email = mail,
            Company = comp,
            Amount = amount ?? e.Dec("total"),
            Status = e.Bool("active") ? "active" : "paused",
            NextInvoiceAt = e.Str("next_run_at"),
        };
    }

    // ---------- sellers ----------

    public async Task<SellerDto> OnboardSellerAsync(OnboardSellerRequest request)
    {
        var company = request.Company?.Trim();
        var contact = request.Contact;
        if (string.IsNullOrEmpty(company))
        {
            throw new ApiProblemException(400, "company is required.");
        }
        if (string.IsNullOrWhiteSpace(contact?.Name))
        {
            throw new ApiProblemException(400, "contact.name is required.");
        }
        if (string.IsNullOrWhiteSpace(contact.Email) || !IsEmail(contact.Email.Trim()))
        {
            throw new ApiProblemException(400, "A valid contact.email is required.");
        }

        var body = new Dictionary<string, object?>
        {
            ["company_name"] = company,
            ["display_name"] = company,
            ["contact_name"] = contact.Name.Trim(),
            ["contact_email"] = contact.Email.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(contact.Phone))
        {
            body["contact_phone"] = contact.Phone.Trim();
        }

        var created = await _stax.CreateSellerMerchantAsync(body);
        return new SellerDto
        {
            SellerId = created.Str("id") ?? string.Empty,
            Company = created.Str("company_name") ?? company,
            Status = created.Str("status"),
        };
    }

    private static bool IsEmail(string s)
    {
        try
        {
            var a = new MailAddress(s);
            return a.Address == s && s.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
