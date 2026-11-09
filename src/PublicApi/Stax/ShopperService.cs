using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Resolves who the caller is (from the JWT) and which Stax customers belong to them.</summary>
public class ShopperService
{
    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly UserManager<ApplicationUser> _users;

    public ShopperService(CatalogContext db, StaxClient stax, UserManager<ApplicationUser> users)
    {
        _db = db;
        _stax = stax;
        _users = users;
    }

    /// <summary>The caller's identity comes from the JWT. Tokens outlive closed accounts, so check the account still exists.</summary>
    public async Task<string> RequireShopperAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw ApiException.Unauthorized("The token carries no identity.");
        }
        if (await _users.FindByNameAsync(name) is null)
        {
            throw ApiException.Unauthorized("This account no longer exists.");
        }
        return name;
    }

    public static bool SameEmail(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Every Stax customer carrying this email (the local link plus whatever Stax knows).</summary>
    public async Task<List<string>> FindCustomerIdsAsync(string email, CancellationToken ct = default)
    {
        var ids = new List<string>();
        var normalized = email.Trim().ToLowerInvariant();

        var link = await _db.ShopperStaxCustomers.FirstOrDefaultAsync(s => s.Email == normalized, ct);
        if (link is not null) ids.Add(link.StaxCustomerId);

        for (var page = 1; page <= 50; page++)
        {
            var result = await _stax.GetAsync($"customer?email={Uri.EscapeDataString(email.Trim())}&per_page=100&page={page}", ct);
            foreach (var c in result.Items())
            {
                var id = c.Str("id");
                if (id is not null && SameEmail(c.Str("email"), email) && c.Str("deleted_at") is null && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
            var last = result["last_page"]?.GetValue<int>() ?? 1;
            if (page >= last) break;
        }
        return ids;
    }

    public async Task<string> GetOrCreateCustomerAsync(string email, string personName, CancellationToken ct = default)
    {
        var ids = await FindCustomerIdsAsync(email, ct);
        var normalized = email.Trim().ToLowerInvariant();
        if (ids.Count > 0)
        {
            await EnsureLinkAsync(normalized, ids[0], ct);
            return ids[0];
        }

        var parts = personName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var customer = await _stax.PostAsync("customer", new JsonObject
        {
            ["firstname"] = parts.Length > 0 ? parts[0] : "Shopper",
            ["lastname"] = parts.Length > 1 ? parts[1] : "",
            ["email"] = email.Trim()
        }, ct);
        var id = customer.Str("id") ?? throw new StaxException(502, "Stax did not return a customer id.");
        await EnsureLinkAsync(normalized, id, ct);
        return id;
    }

    private async Task EnsureLinkAsync(string normalizedEmail, string staxCustomerId, CancellationToken ct)
    {
        var link = await _db.ShopperStaxCustomers.FirstOrDefaultAsync(s => s.Email == normalizedEmail, ct);
        if (link is null)
        {
            _db.ShopperStaxCustomers.Add(new ShopperStaxCustomer { Email = normalizedEmail, StaxCustomerId = staxCustomerId });
        }
        else if (link.StaxCustomerId != staxCustomerId)
        {
            link.StaxCustomerId = staxCustomerId;
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // a concurrent request created the same link; harmless
            _db.ChangeTracker.Clear();
        }
    }
}
