using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateInvoiceScheduleRequest : BaseRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Day of the first invoice (yyyy-MM-dd). Today when omitted.</summary>
    public DateTime? FirstInvoiceDate { get; set; }
    public string? Description { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal? Amount { get; set; }
    public string Interval { get; set; } = "monthly";
    public bool Active { get; set; }
    public DateTimeOffset? NextInvoiceAt { get; set; }
}

public class OnboardSellerRequest : BaseRequest
{
    public string? Company { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

public class SellerDto
{
    public string? SellerId { get; set; }
    public string Company { get; set; } = string.Empty;
    public string? Status { get; set; }
}

/// <summary>Closing a shopper's account.</summary>
public class AccountService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly KeyedLock _locks;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountService(CatalogContext db, StaxGateway stax, KeyedLock locks, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _stax = stax;
        _locks = locks;
        _userManager = userManager;
    }

    public async Task CloseAsync(ApplicationUser user, CancellationToken ct)
    {
        var buyerId = user.UserName!;
        using var _ = await _locks.AcquireAsync(PaymentMethodService.CustomerLockKey(buyerId));

        var payments = await _db.OrderPayments.Where(p => p.BuyerId == buyerId).ToListAsync(ct);
        if (payments.Any(p => p.Status is PaymentStatuses.Pending or PaymentStatuses.Processing
                              || p.RefundStatus == RefundStatuses.Pending))
        {
            throw ApiProblemException.Conflict(
                "The account cannot be closed while a payment or refund is still being processed; try again once it has settled.");
        }

        // Stax first: when it fails nothing was erased locally and the request can simply be repeated.
        var customers = await _db.StaxCustomers.Where(c => c.BuyerId == buyerId).ToListAsync(ct);
        var methods = await _db.SavedPaymentMethods.Where(p => p.BuyerId == buyerId).ToListAsync(ct);
        foreach (var customer in customers)
        {
            var customerId = Guid.Parse(customer.StaxCustomerId);
            var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var item in await _stax.ListCustomerPaymentMethodsAsync(customerId, ct))
                {
                    if (item.Str("id") is { } id)
                    {
                        toDelete.Add(id);
                    }
                }
            }
            catch (StaxException ex) when (ex.StaxStatus == 404)
            {
                // customer already gone
            }

            foreach (var method in methods.Where(m => m.RemovedAt is null && m.StaxCustomerId == customer.StaxCustomerId))
            {
                toDelete.Add(method.StaxPaymentMethodId);
            }

            foreach (var id in toDelete)
            {
                await IgnoreNotFound(() => _stax.DeletePaymentMethodAsync(Guid.Parse(id), ct));
            }

            // Blank the personal data first: Stax only soft-deletes customers.
            await IgnoreNotFound(() => _stax.UpdateCustomerAsync(customerId, new Dictionary<string, object?>
            {
                ["firstname"] = "Closed",
                ["lastname"] = "Account",
                ["email"] = string.Empty,
                ["company"] = string.Empty,
                ["phone"] = string.Empty,
                ["address_1"] = string.Empty,
                ["address_2"] = string.Empty,
                ["address_city"] = string.Empty,
                ["address_state"] = string.Empty,
                ["address_zip"] = string.Empty,
                ["address_country"] = string.Empty,
                ["notes"] = string.Empty,
            }, ct));
            await IgnoreNotFound(() => _stax.DeleteCustomerAsync(customerId, ct));
        }

        // Then the shop.
        var orders = await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == buyerId).ToListAsync(ct);
        var baskets = await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == buyerId).ToListAsync(ct);
        _db.OrderPayments.RemoveRange(payments);
        _db.SavedPaymentMethods.RemoveRange(methods);
        _db.StaxCustomers.RemoveRange(customers);
        _db.Orders.RemoveRange(orders);
        _db.Baskets.RemoveRange(baskets);
        await _db.SaveChangesAsync(ct);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The shop account could not be deleted: "
                                                + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task IgnoreNotFound(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (StaxException ex) when (ex.StaxStatus == 404)
        {
        }
    }
}

/// <summary>Staff features that are only about Stax: monthly invoices and seller onboarding.</summary>
public class BillingService
{
    private const string InvoiceUrl = "https://app.staxpayments.com/#/bill/";

    private readonly StaxGateway _stax;

    public BillingService(StaxGateway stax)
    {
        _stax = stax;
    }

    public async Task<InvoiceScheduleDto> CreateScheduleAsync(CreateInvoiceScheduleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !IsEmail(request.Email))
        {
            throw ApiProblemException.BadRequest("A valid customer e-mail is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Company))
        {
            throw ApiProblemException.BadRequest("The customer's company is required.");
        }

        if (request.Amount <= 0 || request.Amount != Math.Round(request.Amount, 2))
        {
            throw ApiProblemException.BadRequest("amount must be positive, with at most two decimals.");
        }

        var today = DateTime.UtcNow.Date;
        var firstDay = (request.FirstInvoiceDate ?? today).Date;
        if (firstDay < today)
        {
            throw ApiProblemException.Unprocessable("The first invoice cannot be in the past.");
        }

        // Stax wants the start as an instant in the future. "Today" means as soon as possible; a later day
        // starts at noon UTC so that it falls on that calendar day on both sides of the Atlantic.
        var start = firstDay == today
            ? DateTime.UtcNow.AddMinutes(2)
            : firstDay.AddHours(12);
        var rule = $"FREQ=MONTHLY;INTERVAL=1;DTSTART={start:yyyyMMdd'T'HHmm}00Z";

        var email = request.Email.Trim();
        var company = request.Company.Trim();
        var customer = await _stax.FindOrCreateCustomerAsync(email, company, "-", company, ct);
        var customerId = customer.Str("id") ?? throw new ApiProblemException(502, "Stax did not return a customer id.");
        if (!string.Equals(customer.Str("company"), company, StringComparison.Ordinal))
        {
            await _stax.UpdateCustomerAsync(Guid.Parse(customerId), new Dictionary<string, object?> { ["company"] = company }, ct);
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? "Monthly invoice" : request.Description.Trim();
        var schedule = await _stax.CreateInvoiceScheduleAsync(new Dictionary<string, object?>
        {
            ["customer_id"] = customerId,
            ["url"] = InvoiceUrl,
            ["total"] = request.Amount,
            ["rule"] = rule,
            ["meta"] = new Dictionary<string, object?>
            {
                ["subtotal"] = request.Amount,
                ["memo"] = description,
                ["lineItems"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = string.Empty,
                        ["item"] = description,
                        ["details"] = company,
                        ["quantity"] = 1,
                        ["price"] = request.Amount,
                    },
                },
            },
        }, ct);

        var dto = ToDto(schedule);
        dto.CustomerId ??= customerId;
        dto.Email = email;
        dto.Company = company;
        dto.Amount = request.Amount;
        return dto;
    }

    public async Task<InvoiceScheduleDto> SetActiveAsync(string scheduleId, bool active, CancellationToken ct)
    {
        if (!Guid.TryParse(scheduleId, out var id))
        {
            throw ApiProblemException.NotFound("Invoice schedule not found.");
        }

        System.Text.Json.JsonElement schedule;
        try
        {
            schedule = await _stax.SetInvoiceScheduleActiveAsync(id, active, ct);
        }
        catch (StaxException ex) when (ex.StaxStatus == 404 || ex.Message.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiProblemException.NotFound("Invoice schedule not found.");
        }

        var dto = ToDto(schedule);
        if (string.IsNullOrEmpty(dto.ScheduleId))
        {
            dto.ScheduleId = id.ToString("D");
            dto.Active = active;
        }

        if (dto.Active != active)
        {
            throw ApiProblemException.Conflict(
                $"Stax did not {(active ? "resume" : "pause")} the schedule; it is {(dto.Active ? "active" : "paused")}.");
        }

        return dto;
    }

    private static InvoiceScheduleDto ToDto(System.Text.Json.JsonElement schedule) => new()
    {
        ScheduleId = schedule.Str("id") ?? string.Empty,
        CustomerId = schedule.Str("customer_id"),
        Active = schedule.Bool("active") ?? false,
        Amount = schedule.Dec("total"),
        NextInvoiceAt = schedule.Date("next_run_at"),
    };

    public async Task<SellerDto> OnboardSellerAsync(OnboardSellerRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Company))
        {
            throw ApiProblemException.BadRequest("The seller's company is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ContactEmail) || !IsEmail(request.ContactEmail))
        {
            throw ApiProblemException.BadRequest("A valid contact e-mail is required.");
        }

        var body = new Dictionary<string, object?>
        {
            ["company_name"] = request.Company.Trim(),
            ["contact_email"] = request.ContactEmail.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(request.ContactName))
        {
            body["contact_name"] = request.ContactName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ContactPhone))
        {
            body["contact_phone"] = request.ContactPhone.Trim();
        }

        var merchant = await _stax.CreateMerchantAsync(body, ct);
        return new SellerDto
        {
            SellerId = merchant.Str("id"),
            Company = merchant.Str("company_name") ?? request.Company.Trim(),
            Status = merchant.Str("status"),
        };
    }

    private static bool IsEmail(string value)
    {
        try
        {
            var address = new MailAddress(value.Trim());
            return address.Address == value.Trim() && Regex.IsMatch(address.Host, @"\.");
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
