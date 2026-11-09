using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Shopper identity, saved payment methods, the support view of them, and account closing.
/// </summary>
public class ShopperService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly UserManager<ApplicationUser> _users;

    public ShopperService(CatalogContext db, StaxGateway stax, UserManager<ApplicationUser> users)
    {
        _db = db;
        _stax = stax;
        _users = users;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>The caller's e-mail, taken from the JWT. The account must still exist.</summary>
    public async Task<string> ResolveEmailAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiProblemException(401, "The token does not identify a shopper.");
        }
        var user = await _users.FindByNameAsync(name);
        if (user is null)
        {
            throw new ApiProblemException(401, "This shop account no longer exists.");
        }
        return Normalize(user.Email ?? user.UserName ?? name);
    }

    // ---------- Stax customer ----------

    private async Task<ShopperAccount> EnsureAccountAsync(string email)
    {
        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email);
        if (account is not null)
        {
            return account;
        }
        var customerId = await _stax.CreateCustomerAsync(email.Split('@')[0], "Shopper", email, string.Empty);
        account = new ShopperAccount { Email = email, StaxCustomerId = customerId };
        _db.ShopperAccounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    // ---------- saved methods ----------

    public async Task<PaymentMethodDto> SaveMethodAsync(string email, SavePaymentMethodRequest request)
    {
        var account = await EnsureAccountAsync(email);
        StaxPaymentMethod created;

        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            created = await _stax.CreatePaymentMethodFromTokenAsync(request.Token.Trim(), account.StaxCustomerId);
        }
        else
        {
            created = await _stax.CreatePaymentMethodAsync(BuildBody(request, account.StaxCustomerId));
        }

        var record = new SavedPaymentMethod
        {
            ShopperEmail = email,
            StaxPaymentMethodId = created.Id,
            Kind = created.Kind,
            Brand = created.Brand,
            Label = created.Label,
            Last4 = created.Last4,
            CreatedAt = created.CreatedAt ?? DateTimeOffset.UtcNow,
        };
        _db.SavedPaymentMethods.Add(record);
        await _db.SaveChangesAsync();
        return ToDto(record);
    }

    private static IDictionary<string, object?> BuildBody(SavePaymentMethodRequest r, string customerId)
    {
        var type = r.Type?.Trim().ToLowerInvariant();
        if (type is not ("card" or "bank"))
        {
            throw new ApiProblemException(400, "type must be \"card\" or \"bank\".");
        }
        var name = r.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ApiProblemException(400, "name is required.");
        }

        if (type == "card")
        {
            var number = new string((r.CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (number.Length < 12 || number.Length > 19)
            {
                throw new ApiProblemException(400, "cardNumber is required and must have 12 to 19 digits.");
            }
            if (r.ExpMonth is null or < 1 or > 12 || r.ExpYear is null)
            {
                throw new ApiProblemException(400, "expMonth (1-12) and expYear are required.");
            }
            var year = r.ExpYear.Value < 100 ? 2000 + r.ExpYear.Value : r.ExpYear.Value;
            var lastDay = new DateTime(year, r.ExpMonth.Value, 1).AddMonths(1);
            if (lastDay <= DateTime.UtcNow)
            {
                throw new ApiProblemException(400, "The card has expired.");
            }
            var body = new Dictionary<string, object?>
            {
                ["method"] = "card",
                ["person_name"] = name,
                ["card_number"] = number,
                ["card_exp"] = $"{r.ExpMonth.Value:00}{year % 100:00}",
                ["customer_id"] = customerId,
            };
            if (!string.IsNullOrWhiteSpace(r.Cvv)) body["card_cvv"] = r.Cvv.Trim();
            if (!string.IsNullOrWhiteSpace(r.Zip)) body["address_zip"] = r.Zip.Trim();
            return body;
        }

        var account = new string((r.AccountNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        var routing = new string((r.RoutingNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (account.Length < 4 || account.Length > 17)
        {
            throw new ApiProblemException(400, "accountNumber is required and must have 4 to 17 digits.");
        }
        if (routing.Length != 9)
        {
            throw new ApiProblemException(400, "routingNumber is required and must have 9 digits.");
        }
        var accountType = (r.AccountType ?? "checking").Trim().ToLowerInvariant();
        var holderType = (r.HolderType ?? "personal").Trim().ToLowerInvariant();
        if (accountType is not ("checking" or "savings"))
        {
            throw new ApiProblemException(400, "accountType must be \"checking\" or \"savings\".");
        }
        if (holderType is not ("personal" or "business"))
        {
            throw new ApiProblemException(400, "holderType must be \"personal\" or \"business\".");
        }
        return new Dictionary<string, object?>
        {
            ["method"] = "bank",
            ["person_name"] = name,
            ["bank_account"] = account,
            ["bank_routing"] = routing,
            ["bank_type"] = accountType,
            ["bank_holder_type"] = holderType,
            ["customer_id"] = customerId,
        };
    }

    public async Task<List<PaymentMethodDto>> ListMethodsAsync(string email)
    {
        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email);
        if (account is null)
        {
            return new List<PaymentMethodDto>();
        }
        var merged = await SyncMethodsAsync(email, account);
        return merged.Where(m => m.RemovedAt is null).OrderBy(m => m.CreatedAt).Select(ToDto).ToList();
    }

    /// <summary>
    /// Brings the local records in line with Stax: methods Stax lists are recorded, and methods that are no longer
    /// listed (removed through the shop or directly at Stax) get the removal time Stax reports.
    /// </summary>
    private async Task<List<SavedPaymentMethod>> SyncMethodsAsync(string email, ShopperAccount account)
    {
        var active = await _stax.ListCustomerPaymentMethodsAsync(account.StaxCustomerId);
        var local = await _db.SavedPaymentMethods.Where(m => m.ShopperEmail == email).ToListAsync();
        var activeIds = active.Where(a => a.DeletedAt is null).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var a in active.Where(a => a.DeletedAt is null))
        {
            var existing = local.FirstOrDefault(l => string.Equals(l.StaxPaymentMethodId, a.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new SavedPaymentMethod
                {
                    ShopperEmail = email,
                    StaxPaymentMethodId = a.Id,
                    Kind = a.Kind,
                    Brand = a.Brand,
                    Label = a.Label,
                    Last4 = a.Last4,
                    CreatedAt = a.CreatedAt ?? DateTimeOffset.UtcNow,
                };
                _db.SavedPaymentMethods.Add(existing);
                local.Add(existing);
            }
            else if (existing.RemovedAt is not null)
            {
                existing.RemovedAt = null;
            }
        }

        foreach (var l in local.Where(l => !activeIds.Contains(l.StaxPaymentMethodId) && l.RemovedAt is null))
        {
            var atStax = await _stax.GetPaymentMethodAsync(l.StaxPaymentMethodId);
            l.RemovedAt = atStax?.DeletedAt ?? DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync();
        return local;
    }

    public async Task RemoveMethodAsync(string email, string paymentMethodId)
    {
        var record = await FindActiveMethodAsync(email, paymentMethodId);
        try
        {
            await _stax.DeletePaymentMethodAsync(record.StaxPaymentMethodId);
        }
        catch (ApiProblemException ex) when (ex.StatusCode == 404)
        {
            // already gone at Stax
        }
        record.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<SavedPaymentMethod> FindActiveMethodAsync(string email, string paymentMethodId)
    {
        var record = await _db.SavedPaymentMethods.FirstOrDefaultAsync(m =>
            m.ShopperEmail == email && m.StaxPaymentMethodId == paymentMethodId.Trim().ToLower());
        if (record is null || record.RemovedAt is not null)
        {
            throw new ApiProblemException(404, "No such saved payment method.");
        }
        return record;
    }

    // ---------- support ----------

    public async Task<SupportPaymentMethodsResponse> SupportListAsync(string email)
    {
        var normalized = Normalize(email);
        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == normalized);
        var local = await _db.SavedPaymentMethods.Where(m => m.ShopperEmail == normalized).ToListAsync();
        if (account is null && local.Count == 0)
        {
            throw new ApiProblemException(404, "No shopper with that e-mail has saved a payment method.");
        }

        if (account is not null)
        {
            local = await SyncMethodsAsync(normalized, account);
        }

        return new SupportPaymentMethodsResponse
        {
            Email = normalized,
            PaymentMethods = local.OrderBy(m => m.CreatedAt).Select(m => new SupportPaymentMethodDto
            {
                Id = m.StaxPaymentMethodId,
                Type = m.Kind,
                Brand = m.Brand,
                Last4 = m.Last4,
                Label = m.Label,
                CreatedAt = m.CreatedAt,
                RemovedAt = m.RemovedAt,
            }).ToList(),
        };
    }

    // ---------- closing ----------

    public async Task CloseAccountAsync(string email)
    {
        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email);
        if (account is not null)
        {
            // Erase at Stax first: if that fails nothing has been removed from the shop and the shopper can retry.
            var customerId = account.StaxCustomerId;
            foreach (var pm in await _stax.ListCustomerPaymentMethodsAsync(customerId))
            {
                try
                {
                    await _stax.ScrubPaymentMethodAsync(pm.Id);
                    await _stax.DeletePaymentMethodAsync(pm.Id);
                }
                catch (ApiProblemException ex) when (ex.StatusCode == 404)
                {
                }
            }
            await _stax.ScrubCustomerAsync(customerId);
            await _stax.DeleteCustomerAsync(customerId);
        }

        // Then erase in the shop.
        _db.SavedPaymentMethods.RemoveRange(_db.SavedPaymentMethods.Where(m => m.ShopperEmail == email));
        _db.OrderPayments.RemoveRange(_db.OrderPayments.Where(p => p.BuyerEmail == email));
        var orders = await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == email).ToListAsync();
        _db.Orders.RemoveRange(orders);
        var baskets = await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == email).ToListAsync();
        _db.Baskets.RemoveRange(baskets);
        if (account is not null)
        {
            _db.ShopperAccounts.Remove(account);
        }
        await _db.SaveChangesAsync();

        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);
        if (user is not null)
        {
            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new ApiProblemException(409, "The shop account could not be removed: "
                    + string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private static PaymentMethodDto ToDto(SavedPaymentMethod m) => new()
    {
        Id = m.StaxPaymentMethodId,
        Type = m.Kind,
        Brand = m.Brand,
        Last4 = m.Last4,
        Label = m.Label,
        CreatedAt = m.CreatedAt,
    };
}
