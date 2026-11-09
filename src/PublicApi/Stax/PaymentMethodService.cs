using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>The caller, as established by the JWT.</summary>
public record Shopper(string BuyerId, string Email);

public class ShopperAccess
{
    private readonly UserManager<ApplicationUser> _users;

    public ShopperAccess(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    /// <summary>
    /// The identity comes from the JWT only. A token whose account has been closed no longer identifies anyone.
    /// </summary>
    public async Task<Shopper?> FindAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var user = await _users.FindByNameAsync(name);
        if (user is null) return null;
        return new Shopper(user.UserName ?? name, user.Email ?? user.UserName ?? name);
    }

    /// <summary>Resolves a shopper by e-mail for staff; null when no such shopper exists.</summary>
    public async Task<string?> FindBuyerIdByEmailAsync(string email)
    {
        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);
        return user?.UserName;
    }
}

public record SavePaymentMethodInput(
    string? Method, string? PersonName,
    string? CardNumber, string? CardExp, int? ExpMonth, int? ExpYear, string? CardCvv,
    string? BankName, string? BankType, string? BankHolderType, string? BankAccount, string? BankRouting,
    string? Address1, string? Address2, string? City, string? State, string? Zip, string? Country);

public record PaymentMethodView(string Id, string Method, string? Brand, string? Last4, string? Nickname, DateTimeOffset CreatedAt);

public record SupportPaymentMethodView(string Id, string Method, string? Brand, string? Last4, string? Nickname,
    DateTimeOffset? CreatedAt, bool Removed, DateTimeOffset? RemovedAt);

public class PaymentMethodService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;

    public PaymentMethodService(CatalogContext db, StaxGateway stax)
    {
        _db = db;
        _stax = stax;
    }

    /// <summary>The shopper's Stax customer; created on first use.</summary>
    public async Task<string> EnsureCustomerAsync(Shopper shopper, string? personName, CancellationToken ct)
    {
        var link = await _db.PaymentCustomers.FirstOrDefaultAsync(c => c.BuyerId == shopper.BuyerId, ct);
        if (link is not null) return link.StaxCustomerId;

        var (first, last) = SplitName(personName);
        var customer = await _stax.CreateCustomerAsync(new Dictionary<string, object?>
        {
            ["email"] = shopper.Email,
            ["firstname"] = first,
            ["lastname"] = last
        }, ct);
        var id = customer.Str("id") ?? throw new StaxApiException(0, "Stax did not return a customer id.");
        _db.PaymentCustomers.Add(new PaymentCustomer { BuyerId = shopper.BuyerId, StaxCustomerId = id });
        await _db.SaveChangesAsync(ct);
        return id;
    }

    private static (string First, string Last) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ("", "");
        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }

    public async Task<PaymentMethodView> SaveAsync(Shopper shopper, SavePaymentMethodInput input, CancellationToken ct)
    {
        var method = input.Method?.Trim().ToLowerInvariant();
        if (method is not ("card" or "bank"))
            throw PaymentRequestException.BadRequest("method must be 'card' or 'bank'.");
        if (string.IsNullOrWhiteSpace(input.PersonName))
            throw PaymentRequestException.BadRequest("personName is required.");

        var body = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["person_name"] = input.PersonName!.Trim()
        };
        if (method == "card")
        {
            if (string.IsNullOrWhiteSpace(input.CardNumber))
                throw PaymentRequestException.BadRequest("cardNumber is required for a card.");
            var exp = input.CardExp;
            if (string.IsNullOrWhiteSpace(exp) && input.ExpMonth is not null && input.ExpYear is not null)
                exp = $"{input.ExpMonth:00}{input.ExpYear % 100:00}";
            if (string.IsNullOrWhiteSpace(exp))
                throw PaymentRequestException.BadRequest("cardExp (MMYY) or expMonth and expYear are required for a card.");
            body["card_number"] = new string(input.CardNumber!.Where(char.IsDigit).ToArray());
            body["card_exp"] = exp!.Replace("/", "").Trim();
            if (!string.IsNullOrWhiteSpace(input.CardCvv)) body["card_cvv"] = input.CardCvv;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.BankAccount) || string.IsNullOrWhiteSpace(input.BankRouting))
                throw PaymentRequestException.BadRequest("bankAccount and bankRouting are required for a bank account.");
            body["bank_account"] = input.BankAccount;
            body["bank_routing"] = input.BankRouting;
            body["bank_name"] = string.IsNullOrWhiteSpace(input.BankName) ? "Bank" : input.BankName;
            body["bank_type"] = string.IsNullOrWhiteSpace(input.BankType) ? "checking" : input.BankType!.ToLowerInvariant();
            body["bank_holder_type"] = string.IsNullOrWhiteSpace(input.BankHolderType) ? "personal" : input.BankHolderType!.ToLowerInvariant();
        }
        Put(body, "address_1", input.Address1);
        Put(body, "address_2", input.Address2);
        Put(body, "address_city", input.City);
        Put(body, "address_state", input.State);
        Put(body, "address_zip", input.Zip);
        Put(body, "address_country", input.Country);

        body["customer_id"] = await EnsureCustomerAsync(shopper, input.PersonName, ct);
        var created = await _stax.CreatePaymentMethodAsync(body, ct);
        var id = created.Str("id") ?? throw new StaxApiException(0, "Stax did not return a payment method id.");

        var record = new SavedPaymentMethod
        {
            BuyerId = shopper.BuyerId,
            StaxPaymentMethodId = id,
            Method = method,
            Brand = method == "card" ? created.Str("card_type") : created.Str("bank_type"),
            Last4 = created.Str("card_last_four"),
            Nickname = created.Str("nickname")
        };
        _db.SavedPaymentMethods.Add(record);
        await _db.SaveChangesAsync(ct);
        return ToView(record);
    }

    private static void Put(Dictionary<string, object?> body, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) body[key] = value;
    }

    private static PaymentMethodView ToView(SavedPaymentMethod m) =>
        new(m.StaxPaymentMethodId, m.Method, m.Brand, m.Last4, m.Nickname, m.CreatedAt);

    public async Task<IReadOnlyList<PaymentMethodView>> ListAsync(Shopper shopper, CancellationToken ct)
    {
        await SyncRemovedAtStaxAsync(shopper.BuyerId, ct);
        var rows = await _db.SavedPaymentMethods
            .Where(m => m.BuyerId == shopper.BuyerId && m.RemovedAt == null)
            .ToListAsync(ct);
        return rows.OrderBy(m => m.CreatedAt).Select(ToView).ToList();
    }

    /// <summary>The shopper's saved method that is still usable; throws a 404 otherwise.</summary>
    public async Task<SavedPaymentMethod> GetActiveAsync(Shopper shopper, string paymentMethodId, CancellationToken ct)
    {
        if (!Guid.TryParse(paymentMethodId, out var parsed))
            throw PaymentRequestException.BadRequest($"'{paymentMethodId}' is not a valid payment method id.");
        paymentMethodId = parsed.ToString();
        var row = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
            m => m.StaxPaymentMethodId == paymentMethodId && m.BuyerId == shopper.BuyerId && m.RemovedAt == null, ct);
        return row ?? throw PaymentRequestException.NotFound("No such saved payment method.");
    }

    public async Task RemoveAsync(Shopper shopper, string paymentMethodId, CancellationToken ct)
    {
        var row = await GetActiveAsync(shopper, paymentMethodId, ct);
        try
        {
            // the shop keeps the brand and last four for support; Stax keeps nothing personal of a removed method
            await _stax.ScrubPaymentMethodAsync(row.StaxPaymentMethodId, ct);
            await _stax.DeletePaymentMethodAsync(row.StaxPaymentMethodId, ct);
        }
        catch (StaxApiException ex) when (ex.StaxStatus is 404 or 422)
        {
            // already removed at Stax is fine; anything else is a real failure
            var at = await _stax.GetPaymentMethodAsync(row.StaxPaymentMethodId, ct);
            if (at is { } pm && pm.Time("deleted_at") is null) throw;
        }
        row.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Notices methods that were removed at Stax by other means, so a ghost method is never offered.
    /// </summary>
    private async Task SyncRemovedAtStaxAsync(string buyerId, CancellationToken ct)
    {
        var link = await _db.PaymentCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, ct);
        if (link is null) return;
        var active = await _stax.ListCustomerPaymentMethodsAsync(link.StaxCustomerId, ct);
        var activeIds = active.Select(a => a.Str("id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locals = await _db.SavedPaymentMethods.Where(m => m.BuyerId == buyerId && m.RemovedAt == null).ToListAsync(ct);
        foreach (var local in locals.Where(l => !activeIds.Contains(l.StaxPaymentMethodId)))
        {
            var pm = await _stax.GetPaymentMethodAsync(local.StaxPaymentMethodId, ct);
            local.RemovedAt = pm?.Time("deleted_at") ?? DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Everything a shopper ever saved, removed methods included with when they were removed. The shop keeps every
    /// method it ever saved; Stax adds the ones that were saved or removed there directly.
    /// </summary>
    public async Task<IReadOnlyList<SupportPaymentMethodView>> SupportViewAsync(string buyerId, CancellationToken ct)
    {
        var link = await _db.PaymentCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, ct);
        var locals = await _db.SavedPaymentMethods.Where(m => m.BuyerId == buyerId).ToListAsync(ct);

        var activeAtStax = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (link is not null)
        {
            foreach (var pm in await _stax.ListCustomerPaymentMethodsAsync(link.StaxCustomerId, ct))
            {
                if (pm.Str("id") is { } id) activeAtStax[id] = pm;
            }
        }

        // saved at Stax directly (not through the shop): remember them from now on
        foreach (var (id, pm) in activeAtStax.Where(kv => locals.All(l => !string.Equals(l.StaxPaymentMethodId, kv.Key, StringComparison.OrdinalIgnoreCase))))
        {
            var added = new SavedPaymentMethod
            {
                BuyerId = buyerId,
                StaxPaymentMethodId = id,
                Method = pm.Str("method") ?? "card",
                Brand = pm.Str("method") == "bank" ? pm.Str("bank_type") : pm.Str("card_type"),
                Last4 = pm.Str("card_last_four"),
                Nickname = pm.Str("nickname"),
                CreatedAt = pm.Time("created_at") ?? DateTimeOffset.UtcNow
            };
            if (await _db.SavedPaymentMethods.AnyAsync(m => m.StaxPaymentMethodId == id, ct)) continue;
            _db.SavedPaymentMethods.Add(added);
            locals.Add(added);
        }

        // removed at Stax directly: take the time Stax removed them
        foreach (var local in locals.Where(l => l.RemovedAt is null && !activeAtStax.ContainsKey(l.StaxPaymentMethodId)))
        {
            var pm = await _stax.GetPaymentMethodAsync(local.StaxPaymentMethodId, ct);
            local.RemovedAt = pm?.Time("deleted_at") ?? DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return locals.OrderBy(m => m.CreatedAt)
            .Select(m => new SupportPaymentMethodView(m.StaxPaymentMethodId, m.Method, m.Brand, m.Last4, m.Nickname,
                m.CreatedAt, m.RemovedAt is not null, m.RemovedAt))
            .ToList();
    }
}
