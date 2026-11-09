using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Team;

namespace Microsoft.eShopWeb.PublicApi.Stax;

// ---- DTOs --------------------------------------------------------------------------------------

public class CloseAccountDto
{
    public bool Closed { get; set; } = true;
    public string Message { get; set; } = "";
}

public class CreateInvoiceScheduleRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>Day of the first invoice (yyyy-MM-dd). Today when omitted.</summary>
    public string? FirstInvoiceDate { get; set; }
    /// <summary>Alias of <see cref="FirstInvoiceDate"/>.</summary>
    public string? StartDate { get; set; }
    public string? Description { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = "";
    /// <summary>active or paused.</summary>
    public string Status { get; set; } = "";
    public string? CustomerId { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal? Amount { get; set; }
    public string Frequency { get; set; } = "monthly";
    public DateTimeOffset? NextInvoiceAt { get; set; }
}

public class ContactRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class CreateSellerRequest
{
    public string? Company { get; set; }
    public ContactRequest? Contact { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

public class SellerDto
{
    public string SellerId { get; set; } = "";
    public string? Company { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? Status { get; set; }
}

// ---- Feature 7: close an account ---------------------------------------------------------------

public class AccountService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly StaxCustomers _customers;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<AccountService> _logger;

    public AccountService(CatalogContext db, StaxGateway stax, StaxCustomers customers,
        UserManager<ApplicationUser> users, ILogger<AccountService> logger)
    {
        _db = db;
        _stax = stax;
        _customers = customers;
        _users = users;
        _logger = logger;
    }

    public async Task<CloseAccountDto> CloseAsync(string email, CancellationToken ct)
    {
        email = StaxCustomers.Normalize(email);

        var inFlight = await _db.Orders.AnyAsync(o => o.BuyerId == email &&
            (o.PaymentState == PaymentState.Pending || o.RefundState == RefundState.Pending), ct);
        if (inFlight)
        {
            throw new RequestRefusedException(409,
                "The account cannot be closed yet: a bank payment or a refund is still being processed. Try again once it has completed.");
        }

        // ---- erase at Stax first, so that a failure leaves everything in place for a retry
        var customerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in await _db.ShopperProfiles.Where(p => p.Email == email).ToListAsync(ct))
        {
            customerIds.Add(p.StaxCustomerId);
        }

        foreach (var c in await _customers.FindByEmailAsync(email, ct))
        {
            customerIds.Add(c.Str("id")!);
        }

        foreach (var customerId in customerIds)
        {
            await EraseCustomerAsync(customerId, ct);
        }

        // ---- erase in the shop
        var methods = await _db.SavedPaymentMethods.Where(m => m.ShopperEmail == email).ToListAsync(ct);
        _db.SavedPaymentMethods.RemoveRange(methods);
        _db.ShopperProfiles.RemoveRange(await _db.ShopperProfiles.Where(p => p.Email == email).ToListAsync(ct));

        var baskets = await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == email).ToListAsync(ct);
        _db.Baskets.RemoveRange(baskets);

        var orders = await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == email).ToListAsync(ct);
        foreach (var order in orders)
        {
            if (order.PaymentTransactionId is null)
            {
                _db.Orders.Remove(order);
            }
            else
            {
                // the financial record stays, without the person
                order.Anonymize("closed-" + Guid.NewGuid().ToString("N"));
            }
        }

        await _db.SaveChangesAsync(ct);

        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);
        if (user is not null)
        {
            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogError("Could not delete the identity of a closed account: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                throw new RequestRefusedException(409, "The account data was erased but the login could not be removed. Try again.");
            }
        }

        return new CloseAccountDto { Message = "Your account was closed and your data was erased." };
    }

    private async Task EraseCustomerAsync(string customerId, CancellationToken ct)
    {
        if (!Guid.TryParse(customerId, out var customerGuid))
        {
            return;
        }

        try
        {
            var methods = await StaxCall.JsonList(() => _stax.Shop.Customer.GetCustomerPaymentMethods(
                new GetCustomerPaymentMethodsRequest { Id = customerGuid }, cancellationToken: ct));
            foreach (var pm in methods)
            {
                if (pm.Str("deleted_at") is not null || !Guid.TryParse(pm.Str("id"), out var pmId))
                {
                    continue;
                }

                try
                {
                    // best effort: blank the name Stax keeps on the method
                    await StaxCall.Run(() => _stax.Shop.PaymentMethod.PutPaymentMethod(new PutPaymentMethodRequest
                    {
                        Id = pmId,
                        Body = new Dictionary<string, object?> { ["person_name"] = "Closed Account" }
                    }, cancellationToken: ct));
                }
                catch (RequestRefusedException ex) when (StaxCall.IsStaxRefusal(ex))
                {
                    _logger.LogDebug(ex, "Could not blank payment method {Id} before removal", pmId);
                }

                await StaxCall.Run(() => _stax.Shop.PaymentMethod.DeletePaymentMethod(
                    new DeletePaymentMethodRequest { Id = pmId }, cancellationToken: ct));
            }

            // Stax keeps a deleted customer's record, so the personal details are blanked first
            await StaxCall.Run(() => _stax.Shop.Customer.PutCustomer(new PutCustomerRequest
            {
                Id = customerGuid,
                Body = new Dictionary<string, object?>
                {
                    ["firstname"] = "Closed",
                    ["lastname"] = "Account",
                    ["company"] = "",
                    ["email"] = "",
                    ["phone"] = "",
                    ["address_1"] = "",
                    ["address_2"] = "",
                    ["address_city"] = "",
                    ["address_state"] = "",
                    ["address_zip"] = "",
                    ["address_country"] = "",
                    ["notes"] = null,
                    ["reference"] = ""
                }
            }, cancellationToken: ct));
            await StaxCall.Run(() => _stax.Shop.Customer.DeleteCustomer(
                new DeleteCustomerRequest { Id = customerGuid }, cancellationToken: ct));
        }
        catch (RequestRefusedException ex) when (ex.StatusCode == 404)
        {
            // already gone at Stax
        }
    }
}

// ---- Feature 9: monthly invoices ---------------------------------------------------------------

public class InvoiceScheduleService
{
    /// <summary>Where customers open an invoice to pay it (Stax's hosted payment page).</summary>
    private const string InvoicePageUrl = "https://omni.fattmerchant.com/#/bill/";

    private static readonly Regex DtStart = new(@"DTSTART=(\d{8}T\d{6}Z)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly StaxGateway _stax;
    private readonly StaxCustomers _customers;

    public InvoiceScheduleService(StaxGateway stax, StaxCustomers customers)
    {
        _stax = stax;
        _customers = customers;
    }

    public async Task<InvoiceScheduleDto> CreateAsync(CreateInvoiceScheduleRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !IsEmail(email))
        {
            throw new RequestRefusedException(400, "A valid customer e-mail is required.");
        }

        var company = request.Company?.Trim();
        if (string.IsNullOrEmpty(company))
        {
            throw new RequestRefusedException(400, "The customer's company is required.");
        }

        if (request.Amount is not { } amount || amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            throw new RequestRefusedException(400, "amount must be a positive amount with at most two decimals.");
        }

        var start = FirstRun(request.FirstInvoiceDate ?? request.StartDate, DateTimeOffset.UtcNow);

        // the customer
        var existing = (await _customers.FindByEmailAsync(email, ct)).FirstOrDefault();
        string customerId;
        if (existing.ValueKind == JsonValueKind.Object)
        {
            customerId = existing.Str("id")!;
            if (!string.Equals(existing.Str("company"), company, StringComparison.Ordinal))
            {
                await StaxCall.Run(() => _stax.Shop.Customer.PutCustomer(new PutCustomerRequest
                {
                    Id = Guid.Parse(customerId),
                    Body = new Dictionary<string, object?> { ["company"] = company }
                }, cancellationToken: ct));
            }
        }
        else
        {
            var created = await StaxCall.Json(() => _stax.Shop.Customer.PostCustomer(new PostCustomerRequest
            {
                Body = new Dictionary<string, object?> { ["email"] = email, ["company"] = company, ["firstname"] = "", ["lastname"] = "" }
            }, cancellationToken: ct));
            customerId = created.Str("id") ?? throw new RequestRefusedException(502, "Stax did not return the new customer's id.");
        }

        var memo = string.IsNullOrWhiteSpace(request.Description) ? "Monthly invoice" : request.Description!.Trim();
        var capture = new StaxCall.BodyCapture();
        await StaxCall.Run(() => _stax.Shop.InvoiceSchedule.PostInvoiceSchedule(new PostInvoiceScheduleRequest
        {
            Body = new Dictionary<string, object?>
            {
                ["customer_id"] = customerId,
                ["url"] = InvoicePageUrl,
                ["total"] = amount,
                ["rule"] = RuleFor(start),
                ["meta"] = new Dictionary<string, object?>
                {
                    ["subtotal"] = amount,
                    ["tax"] = 0,
                    ["memo"] = memo,
                    ["lineItems"] = new[]
                    {
                        new Dictionary<string, object?> { ["item"] = memo, ["details"] = company, ["quantity"] = 1, ["price"] = amount }
                    }
                }
            }
        }, capture.Options, ct));

        return Map(capture.Element(), email, company);
    }

    public async Task<InvoiceScheduleDto> PauseAsync(string scheduleId, CancellationToken ct)
    {
        var id = ParseId(scheduleId);
        var schedule = await GetAsync(id, ct);
        if (!IsActive(schedule))
        {
            throw new RequestRefusedException(409, "The schedule is already paused.");
        }

        return await UpdateAsync(id, new Dictionary<string, object?> { ["active"] = false }, ct);
    }

    public async Task<InvoiceScheduleDto> ResumeAsync(string scheduleId, CancellationToken ct)
    {
        var id = ParseId(scheduleId);
        var schedule = await GetAsync(id, ct);
        if (IsActive(schedule))
        {
            throw new RequestRefusedException(409, "The schedule is not paused.");
        }

        // Stax would bill every period that was missed while paused if the old start date were kept,
        // so the schedule continues from the next due day instead.
        var body = new Dictionary<string, object?> { ["active"] = true };
        var rule = schedule.Str("rule");
        if (rule is not null && DtStart.Match(rule) is { Success: true } m)
        {
            var original = ParseRuleDate(m.Groups[1].Value);
            var next = NextOccurrence(original, DateTimeOffset.UtcNow.AddMinutes(2));
            if (next != original)
            {
                body["rule"] = DtStart.Replace(rule, "DTSTART=" + next.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
            }
        }

        return await UpdateAsync(id, body, ct);
    }

    private async Task<InvoiceScheduleDto> UpdateAsync(Guid id, Dictionary<string, object?> body, CancellationToken ct)
    {
        var capture = new StaxCall.BodyCapture();
        await StaxCall.Run(() => _stax.Shop.InvoiceSchedule.PutInvoiceSchedule(
            new PutInvoiceScheduleRequest { Id = id, Body = body }, capture.Options, ct));
        return Map(capture.Element(), null, null);
    }

    private async Task<JsonElement> GetAsync(Guid id, CancellationToken ct)
    {
        var capture = new StaxCall.BodyCapture();
        try
        {
            await StaxCall.Run(() => _stax.Shop.InvoiceSchedule.GetInvoiceSchedule(
                new GetInvoiceScheduleRequest { Id = id }, capture.Options, ct));
        }
        catch (RequestRefusedException ex) when (ex.StatusCode is 404 or 422)
        {
            throw new RequestRefusedException(404, "Invoice schedule not found.");
        }

        var schedule = capture.Element();
        if (schedule.Str("deleted_at") is not null)
        {
            throw new RequestRefusedException(404, "Invoice schedule not found.");
        }

        return schedule;
    }

    private static Guid ParseId(string scheduleId) =>
        Guid.TryParse(scheduleId, out var id) ? id : throw new RequestRefusedException(404, "Invoice schedule not found.");

    private static bool IsActive(JsonElement schedule) => schedule.Bool("active") ?? false;

    private static InvoiceScheduleDto Map(JsonElement s, string? email, string? company)
    {
        var customer = s.Prop("customer");
        var next = s.Date("next_run_at");
        return new InvoiceScheduleDto
        {
            ScheduleId = s.Str("id") ?? "",
            Status = IsActive(s) ? "active" : "paused",
            CustomerId = s.Str("customer_id"),
            Email = email ?? customer?.Str("email"),
            Company = company ?? customer?.Str("company"),
            Amount = s.Dec("total"),
            NextInvoiceAt = IsActive(s) ? next : null
        };
    }

    /// <summary>
    /// When the first invoice goes out. Stax refuses a start in the past, so "today" means a moment from now.
    /// </summary>
    internal static DateTimeOffset FirstRun(string? requestedDay, DateTimeOffset now)
    {
        var soon = new DateTimeOffset(now.UtcDateTime.AddMinutes(2).AddTicks(-(now.UtcDateTime.AddMinutes(2).Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);
        if (string.IsNullOrWhiteSpace(requestedDay))
        {
            return soon;
        }

        if (!DateTimeOffset.TryParse(requestedDay.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            throw new RequestRefusedException(400, "firstInvoiceDate must be a date such as 2026-12-01.");
        }

        var day = parsed.UtcDateTime.Date;
        var today = now.UtcDateTime.Date;
        if (day < today.AddDays(-1))
        {
            throw new RequestRefusedException(400, "The first invoice cannot be in the past.");
        }

        // a day at most one day behind UTC is still "today" for a caller in a western time zone
        return day <= today ? soon : new DateTimeOffset(day, TimeSpan.Zero);
    }

    internal static string RuleFor(DateTimeOffset start) =>
        "FREQ=MONTHLY;DTSTART=" + start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseRuleDate(string value) =>
        new(DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), TimeSpan.Zero);

    /// <summary>The first monthly occurrence of the series that starts at <paramref name="start"/> and lies after <paramref name="after"/>.</summary>
    internal static DateTimeOffset NextOccurrence(DateTimeOffset start, DateTimeOffset after)
    {
        if (start > after)
        {
            return start;
        }

        for (var n = 1; n < 1200; n++)
        {
            var candidate = start.AddMonths(n);
            // keep the original day of the month: a short month is skipped rather than shifting the series
            if (candidate > after && candidate.Day == start.Day)
            {
                return candidate;
            }
        }

        return start;
    }

    private static bool IsEmail(string value)
    {
        try
        {
            return new MailAddress(value).Address == value && value.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

// ---- Feature 10: sellers -----------------------------------------------------------------------

public class SellerService
{
    private readonly StaxGateway _stax;

    public SellerService(StaxGateway stax)
    {
        _stax = stax;
    }

    public async Task<SellerDto> OnboardAsync(CreateSellerRequest request, CancellationToken ct)
    {
        var company = request.Company?.Trim();
        var name = (request.Contact?.Name ?? request.ContactName)?.Trim();
        var email = (request.Contact?.Email ?? request.ContactEmail)?.Trim();
        var phone = (request.Contact?.Phone ?? request.ContactPhone)?.Trim();
        if (string.IsNullOrEmpty(company))
        {
            throw new RequestRefusedException(400, "The seller's company is required.");
        }

        if (string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            throw new RequestRefusedException(400, "A valid contact e-mail is required.");
        }

        // Only the platform key may create merchants; without one the shop cannot onboard sellers.
        var platform = _stax.Platform;
        var body = new Dictionary<string, object?>
        {
            ["company_name"] = company,
            ["contact_email"] = email
        };
        if (!string.IsNullOrEmpty(name))
        {
            body["contact_name"] = name;
        }

        if (!string.IsNullOrEmpty(phone))
        {
            body["contact_phone"] = phone;
        }

        var created = await StaxCall.Json(() => platform.Team.PostMerchant(new PostMerchantRequest { Body = body }, cancellationToken: ct));
        return new SellerDto
        {
            SellerId = created.Str("id") ?? throw new RequestRefusedException(502, "Stax did not return the new merchant's id."),
            Company = created.Str("company_name") ?? company,
            ContactName = created.Str("contact_name") ?? name,
            ContactEmail = created.Str("contact_email") ?? email,
            Status = created.Str("status")
        };
    }
}
