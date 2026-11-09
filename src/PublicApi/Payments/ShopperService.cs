using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Resolves the calling shopper and their customer record at Stax.</summary>
public class ShopperService
{
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    private readonly UserManager<ApplicationUser> _users;
    private readonly PaymentsDbContext _db;
    private readonly StaxClient _stax;

    public ShopperService(UserManager<ApplicationUser> users, PaymentsDbContext db, StaxClient stax)
    {
        _users = users;
        _db = db;
        _stax = stax;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// The caller's identity comes from the JWT. A token for an account that has since been
    /// closed is no longer accepted.
    /// </summary>
    public async Task<string> RequireShopperAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException(401, "The token does not identify a shopper.");
        var user = await _users.FindByNameAsync(name);
        if (user == null)
            throw new ApiException(401, "This shop account does not exist (it may have been closed).");
        return NormalizeEmail(user.Email ?? user.UserName ?? name);
    }

    public async Task<string?> FindCustomerIdAsync(string email, CancellationToken ct = default)
    {
        var map = await _db.Shoppers.AsNoTracking().FirstOrDefaultAsync(s => s.Email == email, ct);
        return map?.StaxCustomerId;
    }

    public async Task<string> GetOrCreateCustomerIdAsync(string email, CancellationToken ct = default)
    {
        var existing = await FindCustomerIdAsync(email, ct);
        if (existing != null) return existing;

        await CreateLock.WaitAsync(ct);
        try
        {
            existing = await FindCustomerIdAsync(email, ct);
            if (existing != null) return existing;

            var local = email.Split('@')[0];
            var created = await _stax.PostAsync("customer", new
            {
                firstname = local,
                lastname = "",
                email,
                reference = "eshop-shopper"
            }, ct);
            var id = created["id"]?.GetValue<string>()
                     ?? throw new ApiException(502, "Stax did not return a customer id.");
            _db.Shoppers.Add(new ShopperCustomer { Email = email, StaxCustomerId = id });
            await _db.SaveChangesAsync(ct);
            return id;
        }
        finally
        {
            CreateLock.Release();
        }
    }

    /// <summary>Every Stax customer whose email is exactly this one (Stax's filter is a loose match).</summary>
    public async Task<List<JsonNode>> FindStaxCustomersAsync(string email, CancellationToken ct = default)
    {
        var rows = await _stax.GetAllPagesAsync("customer", new Dictionary<string, string?> { ["email"] = email }, ct);
        return rows.Where(c => string.Equals(c["email"]?.GetValue<string>()?.Trim(), email, StringComparison.OrdinalIgnoreCase)
                               && c["deleted_at"] == null).ToList();
    }

    /// <summary>Stax timestamps are UTC "yyyy-MM-dd HH:mm:ss".</summary>
    public static DateTimeOffset? ParseStaxTime(JsonNode? node)
    {
        var s = node is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }
}
