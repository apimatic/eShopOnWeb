using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateInvoiceScheduleRequest
{
    public string? Email { get; set; }
    public string? Company { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Day of the first invoice (yyyy-MM-dd). Today when omitted.</summary>
    public DateTime? FirstInvoiceDate { get; set; }
}

public class InvoiceScheduleDto
{
    public string ScheduleId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Company { get; set; } = "";
    public decimal Amount { get; set; }
    public string Frequency { get; set; } = "monthly";
    public string FirstInvoiceDate { get; set; } = "";
    public string? NextInvoiceAt { get; set; }
    public bool Active { get; set; }
    public string Status { get; set; } = "";
}

public class InvoiceScheduleService
{
    private readonly PaymentsDbContext _db;
    private readonly StaxGateway _stax;
    private readonly BaseUrlConfiguration _baseUrls;

    public InvoiceScheduleService(PaymentsDbContext db, StaxGateway stax, IOptions<BaseUrlConfiguration> baseUrls)
    {
        _db = db;
        _stax = stax;
        _baseUrls = baseUrls.Value;
    }

    public async Task<InvoiceScheduleDto> Create(CreateInvoiceScheduleRequest r, CancellationToken ct = default)
    {
        PaymentMethodService.RequireEmail(r.Email, "email");
        if (string.IsNullOrWhiteSpace(r.Company)) throw new PaymentApiException(400, "company is required.");
        if (r.Amount <= 0 || decimal.Round(r.Amount, 2) != r.Amount) throw new PaymentApiException(400, "amount must be a positive amount with at most two decimals.");
        var email = r.Email!.Trim().ToLowerInvariant();
        var company = r.Company.Trim();

        var now = DateTime.UtcNow;
        var today = now.Date;
        var firstDay = (r.FirstInvoiceDate ?? today).Date;
        if (firstDay < today) throw new PaymentApiException(422, "The first invoice day cannot be in the past.");

        // today: Stax only accepts a start that has not passed yet, so the first invoice is cut a minute from now
        var start = firstDay == today ? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc).AddMinutes(1) : firstDay;
        if (start.Date != firstDay) start = firstDay.AddDays(1);
        var rule = BuildRule(start);

        StaxSchedule schedule;
        try
        {
            var customer = await _stax.FindOrCreateCustomer(email, ShopperService.FirstName(email), null, company, ct);
            if (!string.Equals(customer.Company, company, StringComparison.Ordinal)) await _stax.SetCustomerCompany(customer.Id, company, ct);
            var url = (_baseUrls.WebBase ?? "https://localhost/").TrimEnd('/') + "/invoices";
            schedule = await _stax.CreateInvoiceSchedule(customer.Id, r.Amount, url, rule, ct);
            _db.InvoiceSchedules.Add(new InvoiceScheduleRecord
            {
                StaxScheduleId = schedule.Id, StaxCustomerId = customer.Id, Email = email, Company = company, Amount = r.Amount,
                FirstInvoiceDate = start.Date, Active = schedule.Active,
            });
        }
        catch (StaxException ex) { throw ex.ToApi("Creating the invoice schedule"); }
        await _db.SaveChangesAsync(ct);

        return new InvoiceScheduleDto
        {
            ScheduleId = schedule.Id, Email = email, Company = company, Amount = r.Amount, FirstInvoiceDate = start.ToString("yyyy-MM-dd"),
            NextInvoiceAt = schedule.NextRunAt, Active = schedule.Active, Status = schedule.Active ? "active" : "paused",
        };
    }

    /// <summary>Monthly on the start day; a start on the 29th-31st means "that day, or the month's last day when it is shorter".</summary>
    private static string BuildRule(DateTime start)
    {
        var dtstart = start.ToString("yyyyMMdd'T'HHmmss'Z'");
        return start.Day <= 28
            ? $"FREQ=MONTHLY;INTERVAL=1;DTSTART={dtstart}"
            : $"FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY={string.Join(',', Enumerable.Range(28, start.Day - 27))};BYSETPOS=-1;DTSTART={dtstart}";
    }

    public async Task<InvoiceScheduleDto> SetActive(string scheduleId, bool active, CancellationToken ct = default)
    {
        var record = await _db.InvoiceSchedules.FirstOrDefaultAsync(s => s.StaxScheduleId == scheduleId, ct)
            ?? throw new PaymentApiException(404, "Invoice schedule not found.");
        StaxSchedule schedule;
        try { schedule = await _stax.SetInvoiceScheduleActive(record.StaxScheduleId, active, ct); }
        catch (StaxException ex) { throw ex.ToApi(active ? "Resuming the invoice schedule" : "Pausing the invoice schedule"); }
        record.Active = schedule.Active;
        await _db.SaveChangesAsync(ct);
        return new InvoiceScheduleDto
        {
            ScheduleId = record.StaxScheduleId, Email = record.Email, Company = record.Company, Amount = record.Amount,
            FirstInvoiceDate = record.FirstInvoiceDate.ToString("yyyy-MM-dd"), NextInvoiceAt = schedule.NextRunAt,
            Active = record.Active, Status = record.Active ? "active" : "paused",
        };
    }
}

public class OnboardSellerRequest
{
    public string? Company { get; set; }
    public SellerContact? Contact { get; set; }
}

public class SellerContact
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class SellerDto
{
    public string SellerId { get; set; } = "";
    public string Company { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string? Status { get; set; }
}

public class SellerService
{
    private readonly PaymentsDbContext _db;
    private readonly StaxGateway _stax;

    public SellerService(PaymentsDbContext db, StaxGateway stax)
    {
        _db = db;
        _stax = stax;
    }

    public async Task<SellerDto> Onboard(OnboardSellerRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Company)) throw new PaymentApiException(400, "company is required.");
        if (r.Contact == null) throw new PaymentApiException(400, "contact is required.");
        if (string.IsNullOrWhiteSpace(r.Contact.Name)) throw new PaymentApiException(400, "contact.name is required.");
        PaymentMethodService.RequireEmail(r.Contact.Email, "contact.email");

        var body = new Dictionary<string, object?>
        {
            ["company_name"] = r.Company.Trim(),
            ["contact_name"] = r.Contact.Name.Trim(),
            ["contact_email"] = r.Contact.Email!.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(r.Contact.Phone)) body["contact_phone"] = r.Contact.Phone.Trim();

        (string Id, string? Status) merchant;
        try { merchant = await _stax.CreateMerchant(body, ct); }
        catch (StaxException ex) { throw ex.ToApi("Onboarding the seller"); }

        _db.Sellers.Add(new SellerRecord { StaxMerchantId = merchant.Id, Company = r.Company.Trim(), ContactEmail = r.Contact.Email.Trim() });
        await _db.SaveChangesAsync(ct);
        return new SellerDto { SellerId = merchant.Id, Company = r.Company.Trim(), ContactEmail = r.Contact.Email.Trim(), Status = merchant.Status };
    }
}

public class AccountService
{
    private readonly PaymentsDbContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxGateway _stax;
    private readonly UserManager<ApplicationUser> _users;

    public AccountService(PaymentsDbContext db, CatalogContext catalog, StaxGateway stax, UserManager<ApplicationUser> users)
    {
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _users = users;
    }

    /// <summary>Erases the shopper at Stax first (so a failure there can be retried), then everything the shop holds.</summary>
    public async Task Close(string email, CancellationToken ct = default)
    {
        if (await _db.OrderPayments.AnyAsync(o => o.BuyerEmail == email && o.RefundState == RefundState.Pending, ct))
            throw new PaymentApiException(409, "The account cannot be closed while a refund to the shopper is still pending.");

        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email, ct);
        var customerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (account != null) customerIds.Add(account.StaxCustomerId);

        try
        {
            var found = await _stax.FindCustomerByEmail(email, ct);
            if (found != null) customerIds.Add(found.Id);

            var methodIds = new HashSet<string>(await _db.SavedPaymentMethods.Where(s => s.Email == email).Select(s => s.StaxPaymentMethodId).ToListAsync(ct),
                StringComparer.OrdinalIgnoreCase);
            foreach (var customerId in customerIds)
                foreach (var pm in await _stax.ListPaymentMethods(customerId, ct)) methodIds.Add(pm.Id);

            foreach (var id in methodIds)
            {
                var pm = await _stax.GetPaymentMethod(id, ct);
                if (pm == null) continue;
                await Ignore404(() => _stax.ScrubPaymentMethod(id, ct));
                if (pm.DeletedAt == null) await Ignore404(() => _stax.DeletePaymentMethod(id, ct));
            }

            foreach (var schedule in await _db.InvoiceSchedules.Where(s => s.Email == email).ToListAsync(ct))
                await Ignore404(() => _stax.DeleteInvoiceSchedule(schedule.StaxScheduleId, ct));

            foreach (var customerId in customerIds) await Ignore404(() => _stax.EraseCustomer(customerId, ct));
        }
        catch (StaxException ex) { throw ex.ToApi("Erasing the shopper at Stax"); }

        // the shop's own data
        var orders = await _catalog.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == email).ToListAsync(ct);
        _catalog.Orders.RemoveRange(orders);
        var baskets = await _catalog.Baskets.Include(b => b.Items).Where(b => b.BuyerId == email).ToListAsync(ct);
        _catalog.Baskets.RemoveRange(baskets);
        await _catalog.SaveChangesAsync(ct);

        _db.OrderPayments.RemoveRange(_db.OrderPayments.Where(o => o.BuyerEmail == email));
        _db.SavedPaymentMethods.RemoveRange(_db.SavedPaymentMethods.Where(s => s.Email == email));
        _db.InvoiceSchedules.RemoveRange(_db.InvoiceSchedules.Where(s => s.Email == email));
        _db.ShopperAccounts.RemoveRange(_db.ShopperAccounts.Where(a => a.Email == email));
        await _db.SaveChangesAsync(ct);

        var user = await _users.FindByNameAsync(email) ?? await _users.FindByEmailAsync(email);
        if (user != null) await _users.DeleteAsync(user);
    }

    private static async Task Ignore404(Func<Task> call)
    {
        try { await call(); }
        catch (StaxException ex) when (ex.UpstreamStatus == 404) { }
    }
}
