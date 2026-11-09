using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Resolves who the caller is (from the JWT) and their Stax customer record.</summary>
public class ShopperService
{
    private static readonly SemaphoreSlim CustomerLock = new(1, 1);

    private readonly StaxContext _db;
    private readonly StaxClient _stax;

    public ShopperService(StaxContext db, StaxClient stax)
    {
        _db = db;
        _stax = stax;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string HashEmail(string email)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeEmail(email)))).ToLowerInvariant();

    /// <summary>The shopper's identity comes from the JWT, never from the request.</summary>
    public async Task<string> RequireShopperAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirst(ClaimTypes.Name)?.Value
                   ?? user.FindFirst("name")?.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiException(401, "The token does not identify a shopper.");
        }

        var email = NormalizeEmail(name);
        var hash = HashEmail(email);
        if (await _db.ClosedShoppers.AnyAsync(c => c.EmailHash == hash, ct))
        {
            throw new ApiException(403, "This account has been closed.");
        }
        return email;
    }

    /// <summary>Returns the shopper's Stax customer id, creating the customer on first use.</summary>
    public async Task<string> EnsureCustomerAsync(string email, CancellationToken ct = default)
    {
        var existing = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (existing != null) return existing.StaxCustomerId;

        await CustomerLock.WaitAsync(ct);
        try
        {
            existing = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email, ct);
            if (existing != null) return existing.StaxCustomerId;

            var local = email.Split('@')[0];
            var id = await _stax.CreateCustomerAsync(new { email, firstname = local, reference = "eshop-shopper" }, ct);
            _db.ShopperAccounts.Add(new ShopperAccount { Email = email, StaxCustomerId = id });
            await _db.SaveChangesAsync(ct);
            return id;
        }
        finally
        {
            CustomerLock.Release();
        }
    }

    /// <summary>
    /// Every Stax customer record that belongs to this email: the one the shop created plus any
    /// that already exist at Stax under the email. Customers created for invoice schedules are left out.
    /// </summary>
    public async Task<System.Collections.Generic.List<string>> ResolveCustomerIdsAsync(string email, CancellationToken ct = default)
    {
        var ids = (await _db.ShopperAccounts.Where(a => a.Email == email).Select(a => a.StaxCustomerId).ToListAsync(ct))
            .ToList();
        var found = await _stax.FindCustomerIdsByEmailAsync(email, ct);
        var invoiceCustomers = await _db.InvoiceSchedules.Select(s => s.StaxCustomerId).ToListAsync(ct);
        foreach (var id in found)
        {
            if (!ids.Contains(id) && !invoiceCustomers.Contains(id)) ids.Add(id);
        }
        return ids;
    }
}
