using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.PaymentMethod;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record AddPaymentMethodRequest
{
    /// <summary>"card" or "bank"</summary>
    public string? Method { get; init; }
    public string? PersonName { get; init; }
    // card
    public string? CardNumber { get; init; }
    /// <summary>MMYY, MMYYYY or MM/YY(YY)</summary>
    public string? CardExp { get; init; }
    public string? CardCvv { get; init; }
    public string? AddressZip { get; init; }
    // bank
    public string? BankAccount { get; init; }
    public string? BankRouting { get; init; }
    /// <summary>checking | savings</summary>
    public string? BankType { get; init; }
    /// <summary>personal | business</summary>
    public string? BankHolderType { get; init; }
}

public record PaymentMethodDto(string Id, string Type, string? Brand, string? Last4, int? ExpMonth, int? ExpYear,
    string? BankType, string? HolderType, string? Nickname, DateTime CreatedAt);

public record StaffPaymentMethodDto(string Id, string Type, string? Brand, string? Last4, int? ExpMonth, int? ExpYear,
    string? BankType, string? HolderType, string? Nickname, string? CustomerId, DateTime? CreatedAt, DateTime? RemovedAt);

public sealed class ShopperService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly PaymentsContext _db;
    private readonly StaxGateway _stax;
    private readonly UserManager<ApplicationUser> _users;

    public ShopperService(PaymentsContext db, StaxGateway stax, UserManager<ApplicationUser> users)
    {
        _db = db;
        _stax = stax;
        _users = users;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static SemaphoreSlim LockFor(string key) => Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private static Guid G(string id) =>
        Guid.TryParse(id, out var g) ? g : throw PaymentException.NotFound("Unknown id.");

    // ------------------------------------------------------------------ Stax customers

    /// <summary>Finds (or creates) the Stax customer that represents this e-mail address.</summary>
    public async Task<string> EnsureCustomerAsync(string email, string? company = null)
    {
        email = Normalize(email);
        var gate = LockFor("customer:" + email);
        await gate.WaitAsync();
        try
        {
            var acct = await _db.ShopperAccounts.FindAsync(email);
            string? customerId = null;

            if (acct?.StaxCustomerId != null && await IsLiveCustomerAsync(acct.StaxCustomerId))
            {
                customerId = acct.StaxCustomerId;
            }
            customerId ??= (await FindCustomerIdsAsync(email)).FirstOrDefault();

            if (customerId == null)
            {
                var local = email.Split('@')[0];
                var created = Js.El(await _stax.Call(c => c.Customer.PostCustomer(new PostCustomerRequest
                {
                    Body = new { firstname = local, lastname = "", email, company = company ?? "" }
                })));
                customerId = created.Str("id") ?? throw new PaymentException(502, "Stax did not return a customer id.");
            }
            else if (!string.IsNullOrWhiteSpace(company))
            {
                var current = Js.El(await _stax.Call(c => c.Customer.GetCustomer(new GetCustomerRequest { Id = G(customerId) })));
                if (!string.Equals(current.Str("company"), company, StringComparison.Ordinal))
                {
                    await _stax.Call(c => c.Customer.PutCustomer(new PutCustomerRequest
                    {
                        Id = G(customerId),
                        Body = new { company }
                    }));
                }
            }

            if (acct == null)
            {
                _db.ShopperAccounts.Add(new ShopperAccount { Email = email, StaxCustomerId = customerId, CreatedAt = DateTime.UtcNow });
            }
            else
            {
                acct.StaxCustomerId = customerId;
            }
            await _db.SaveChangesAsync();
            return customerId;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> IsLiveCustomerAsync(string customerId)
    {
        try
        {
            var c = Js.El(await _stax.Call(x => x.Customer.GetCustomer(new GetCustomerRequest { Id = G(customerId) })));
            return c.Str("deleted_at") == null;
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            return false;
        }
    }

    /// <summary>Ids of the live Stax customers carrying exactly this e-mail address.</summary>
    public async Task<List<string>> FindCustomerIdsAsync(string email)
    {
        var ids = new List<string>();
        for (var page = 1; page <= 50; page++)
        {
            var p = Js.El(await _stax.Call(c => c.Customer.GetCustomers(new GetCustomersRequest { Email = email, Page = page, PerPage = 100 })));
            foreach (var cust in Js.Items(p))
            {
                if (cust.Str("deleted_at") == null &&
                    string.Equals(cust.Str("email")?.Trim(), email, StringComparison.OrdinalIgnoreCase) &&
                    cust.Str("id") is { } id)
                {
                    ids.Add(id);
                }
            }
            if (page >= Js.LastPage(p)) break;
        }
        return ids;
    }

    // ------------------------------------------------------------------ saved payment methods

    public async Task<PaymentMethodDto> AddMethodAsync(string email, AddPaymentMethodRequest req)
    {
        email = Normalize(email);
        var body = BuildMethodBody(req, out var type);
        var customerId = await EnsureCustomerAsync(email);
        body["customer_id"] = customerId;

        var created = Js.El(await _stax.Call(c => c.PaymentMethod.PostPaymentMethod(new PostPaymentMethodRequest { Body = body })));
        var id = created.Str("id") ?? throw new PaymentException(502, "Stax did not return a payment method id.");

        var row = new SavedPaymentMethod
        {
            Id = id,
            ShopperEmail = email,
            Type = type,
            CreatedAt = DateTime.UtcNow
        };
        Fill(row, created);
        _db.SavedPaymentMethods.Add(row);
        await _db.SaveChangesAsync();
        return ToDto(row);
    }

    private static Dictionary<string, object?> BuildMethodBody(AddPaymentMethodRequest r, out string type)
    {
        type = (r.Method ?? "").Trim().ToLowerInvariant();
        var body = new Dictionary<string, object?>();
        if (type == "card")
        {
            var number = new string((r.CardNumber ?? "").Where(char.IsDigit).ToArray());
            if (number.Length < 12 || number.Length > 19) throw PaymentException.BadRequest("cardNumber must have 12 to 19 digits.");
            if (!TryParseExpiry(r.CardExp, out var month, out var year)) throw PaymentException.BadRequest("cardExp must be MMYY or MMYYYY.");
            var endOfMonth = new DateTime(year, month, 1).AddMonths(1);
            if (endOfMonth <= DateTime.UtcNow) throw PaymentException.BadRequest("The card has expired.");
            body["method"] = "card";
            body["card_number"] = number;
            body["card_exp"] = $"{month:00}{year % 100:00}";
            if (!string.IsNullOrWhiteSpace(r.CardCvv)) body["card_cvv"] = r.CardCvv.Trim();
            if (!string.IsNullOrWhiteSpace(r.AddressZip)) body["address_zip"] = r.AddressZip.Trim();
        }
        else if (type == "bank")
        {
            var account = new string((r.BankAccount ?? "").Where(char.IsDigit).ToArray());
            var routing = new string((r.BankRouting ?? "").Where(char.IsDigit).ToArray());
            if (account.Length < 4 || account.Length > 17) throw PaymentException.BadRequest("bankAccount must have 4 to 17 digits.");
            if (routing.Length != 9) throw PaymentException.BadRequest("bankRouting must have 9 digits.");
            var bankType = (r.BankType ?? "checking").Trim().ToLowerInvariant();
            var holder = (r.BankHolderType ?? "personal").Trim().ToLowerInvariant();
            if (bankType is not ("checking" or "savings")) throw PaymentException.BadRequest("bankType must be checking or savings.");
            if (holder is not ("personal" or "business")) throw PaymentException.BadRequest("bankHolderType must be personal or business.");
            body["method"] = "bank";
            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = bankType;
            body["bank_holder_type"] = holder;
        }
        else
        {
            throw PaymentException.BadRequest("method must be \"card\" or \"bank\".");
        }

        if (string.IsNullOrWhiteSpace(r.PersonName)) throw PaymentException.BadRequest("personName is required.");
        body["person_name"] = r.PersonName.Trim();
        return body;
    }

    private static bool TryParseExpiry(string? raw, out int month, out int year)
    {
        month = year = 0;
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (d.Length == 4) { year = 2000 + int.Parse(d[2..]); }
        else if (d.Length == 6) { year = int.Parse(d[2..]); }
        else return false;
        month = int.Parse(d[..2]);
        return month is >= 1 and <= 12 && year is >= 2000 and <= 2200;
    }

    private static void Fill(SavedPaymentMethod row, JsonElement pm)
    {
        row.Type = pm.Str("method") ?? row.Type;
        row.Brand = row.Type == "card" ? pm.Str("card_type") : null;
        row.Last4 = pm.Str("card_last_four") ?? pm.Prop("meta")?.Str("accountDisplay");
        row.Nickname = pm.Str("nickname");
        row.BankType = pm.Str("bank_type");
        row.HolderType = pm.Str("bank_holder_type");
        var exp = pm.Str("card_exp");
        if (exp is { Length: 6 } && int.TryParse(exp[..2], out var m) && int.TryParse(exp[2..], out var y))
        {
            row.ExpMonth = m;
            row.ExpYear = y;
        }
    }

    private static PaymentMethodDto ToDto(SavedPaymentMethod r) =>
        new(r.Id, r.Type, r.Brand, r.Last4, r.ExpMonth, r.ExpYear, r.BankType, r.HolderType, r.Nickname, r.CreatedAt);

    public async Task<List<PaymentMethodDto>> ListMethodsAsync(string email)
    {
        email = Normalize(email);
        var rows = await _db.SavedPaymentMethods.Where(m => m.ShopperEmail == email && m.RemovedAt == null).ToListAsync();
        if (rows.Count == 0) return new();

        // Stax is the source of truth: a method removed there no longer counts as saved.
        var acct = await _db.ShopperAccounts.FindAsync(email);
        if (acct?.StaxCustomerId != null)
        {
            var live = (await _stax.Call(c => c.Customer.GetCustomerPaymentMethods(new GetCustomerPaymentMethodsRequest { Id = G(acct.StaxCustomerId) })))
                .Select(Js.El).Where(p => p.Str("deleted_at") == null).Select(p => p.Str("id")).ToHashSet();
            foreach (var gone in rows.Where(r => !live.Contains(r.Id)).ToList())
            {
                gone.RemovedAt = DateTime.UtcNow;
                rows.Remove(gone);
            }
            await _db.SaveChangesAsync();
        }
        return rows.OrderBy(r => r.CreatedAt).Select(ToDto).ToList();
    }

    public async Task RemoveMethodAsync(string email, string paymentMethodId)
    {
        email = Normalize(email);
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(m => m.Id == paymentMethodId && m.ShopperEmail == email && m.RemovedAt == null)
                  ?? throw PaymentException.NotFound("No such saved payment method.");
        try
        {
            await _stax.Call(c => c.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = G(row.Id) }));
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // already gone at Stax
        }
        row.RemovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>The caller's active saved method, or a 4xx explaining why it can't be used.</summary>
    public async Task<SavedPaymentMethod> GetUsableMethodAsync(string email, string? paymentMethodId)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId)) throw PaymentException.BadRequest("paymentMethodId is required.");
        email = Normalize(email);
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(m => m.Id == paymentMethodId && m.ShopperEmail == email)
                  ?? throw PaymentException.NotFound("No such saved payment method.");
        if (row.RemovedAt != null) throw PaymentException.Unprocessable("That payment method has been removed.");
        return row;
    }

    // ------------------------------------------------------------------ support view

    public async Task<List<StaffPaymentMethodDto>> StaffListMethodsAsync(string email)
    {
        email = Normalize(email);
        var customerIds = new HashSet<string>();
        var acct = await _db.ShopperAccounts.FindAsync(email);
        if (acct?.StaxCustomerId != null) customerIds.Add(acct.StaxCustomerId);
        foreach (var id in await FindCustomerIdsAsync(email)) customerIds.Add(id);

        var local = await _db.SavedPaymentMethods.Where(m => m.ShopperEmail == email).ToListAsync();
        if (customerIds.Count == 0 && local.Count == 0)
        {
            throw PaymentException.NotFound("No shopper with that e-mail has saved a payment method.");
        }

        var result = new Dictionary<string, StaffPaymentMethodDto>();
        foreach (var customerId in customerIds)
        {
            for (var page = 1; page <= 100; page++)
            {
                JsonElement p;
                try
                {
                    p = Js.El(await _stax.Call(c => c.PaymentMethod.GetPaymentMethods(new GetPaymentMethodsRequest
                    {
                        CustomerId = G(customerId), Status = "all", Page = page, PerPage = 100
                    })));
                }
                catch (PaymentException ex) when (ex.StatusCode == 404)
                {
                    break;
                }
                foreach (var pm in Js.Items(p))
                {
                    var id = pm.Str("id");
                    if (id == null || pm.Str("customer_id") is { } cid && !string.Equals(cid, customerId, StringComparison.OrdinalIgnoreCase)) continue;
                    var tmp = new SavedPaymentMethod { Type = pm.Str("method") ?? "" };
                    Fill(tmp, pm);
                    var l = local.FirstOrDefault(x => x.Id == id);
                    result[id] = new StaffPaymentMethodDto(id, tmp.Type, tmp.Brand, tmp.Last4, tmp.ExpMonth, tmp.ExpYear,
                        tmp.BankType, tmp.HolderType, tmp.Nickname, customerId,
                        pm.Time("created_at") ?? l?.CreatedAt, pm.Time("deleted_at") ?? l?.RemovedAt);
                }
                if (page >= Js.LastPage(p)) break;
            }
        }

        // methods we know about that Stax no longer lists
        foreach (var l in local.Where(l => !result.ContainsKey(l.Id)))
        {
            result[l.Id] = new StaffPaymentMethodDto(l.Id, l.Type, l.Brand, l.Last4, l.ExpMonth, l.ExpYear, l.BankType, l.HolderType,
                l.Nickname, acct?.StaxCustomerId, l.CreatedAt, l.RemovedAt ?? DateTime.UtcNow);
        }
        return result.Values.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
    }

    // ------------------------------------------------------------------ closing an account

    public async Task CloseAccountAsync(string email)
    {
        email = Normalize(email);
        var gate = LockFor("close:" + email);
        await gate.WaitAsync();
        try
        {
            // 1. erase at Stax: payment methods, schedules, then the customer record itself
            var customerIds = new HashSet<string>();
            var acct = await _db.ShopperAccounts.FindAsync(email);
            if (acct?.StaxCustomerId != null) customerIds.Add(acct.StaxCustomerId);
            foreach (var id in await FindCustomerIdsAsync(email)) customerIds.Add(id);
            foreach (var customerId in customerIds) await EraseStaxCustomerAsync(customerId);

            // 2. erase in the shop
            _db.ShopOrders.RemoveRange(await _db.ShopOrders.Include(o => o.Lines).Where(o => o.BuyerEmail == email).ToListAsync());
            _db.SavedPaymentMethods.RemoveRange(_db.SavedPaymentMethods.Where(m => m.ShopperEmail == email));
            if (acct != null) _db.ShopperAccounts.Remove(acct);
            await _db.SaveChangesAsync();

            var user = await _users.FindByNameAsync(email) ?? await _users.FindByEmailAsync(email);
            if (user != null) await _users.DeleteAsync(user);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EraseStaxCustomerAsync(string customerId)
    {
        var gid = G(customerId);
        try
        {
            for (var page = 1; page <= 100; page++)
            {
                var p = Js.El(await _stax.Call(c => c.PaymentMethod.GetPaymentMethods(new GetPaymentMethodsRequest
                {
                    CustomerId = gid, Status = "all", Page = page, PerPage = 100
                })));
                foreach (var pm in Js.Items(p).Where(x => x.Str("deleted_at") == null))
                {
                    try
                    {
                        await _stax.Call(c => c.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = G(pm.Str("id")!) }));
                    }
                    catch (PaymentException ex) when (ex.StatusCode == 404) { }
                }
                if (page >= Js.LastPage(p)) break;
            }

            var schedules = Js.El(await _stax.Call(c => c.InvoiceSchedule.GetInvoiceSchedules()));
            foreach (var s in Js.Items(schedules).Where(s => string.Equals(s.Str("customer_id"), customerId, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    await _stax.Call(c => c.InvoiceSchedule.DeleteInvoiceSchedule(new DeleteInvoiceScheduleRequest { Id = G(s.Str("id")!) }));
                }
                catch (PaymentException ex) when (ex.StatusCode == 404) { }
            }

            // Stax only soft-deletes customers, so blank the personal details first.
            await _stax.Call(c => c.Customer.PutCustomer(new PutCustomerRequest
            {
                Id = gid,
                Body = new
                {
                    firstname = "Closed", lastname = "Account", company = "", email = "", phone = "", cc_emails = (string?)null,
                    address_1 = "", address_2 = "", address_city = "", address_state = "", address_zip = "", address_country = "",
                    notes = "", reference = ""
                }
            }));
            await _stax.Call(c => c.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = gid }));
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // customer already gone at Stax
        }
    }
}
