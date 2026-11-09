using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Knows who the caller is (from the JWT) and which Stax customer record(s) belong to a shopper.
/// </summary>
public class ShopperDirectory
{
    // Creating the Stax customer of a shopper must not race with itself.
    private static readonly SemaphoreSlim CustomerLock = new(1, 1);

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;

    public ShopperDirectory(CatalogContext db, StaxGateway stax)
    {
        _db = db;
        _stax = stax;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    /// <summary>The shopper behind the JWT: the identity comes from the token alone.</summary>
    public Task<string> RequireShopper(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name
                   ?? principal.FindFirst(ClaimTypes.Name)?.Value
                   ?? principal.FindFirst("unique_name")?.Value;
        if (string.IsNullOrWhiteSpace(name))
            throw new AppException(HttpStatusCode.Unauthorized, "The token does not identify a shopper.");
        return Task.FromResult(Normalize(name));
    }

    /// <summary>The Stax customer of a shopper, if they have one.</summary>
    public async Task<string?> FindCustomerId(string email, CancellationToken ct = default)
    {
        var link = await _db.StaxCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.BuyerEmail == email, ct);
        return link?.StaxCustomerId;
    }

    /// <summary>The Stax customer of a shopper; created at Stax the first time it is needed.</summary>
    public async Task<string> EnsureCustomerId(string email, CancellationToken ct = default)
    {
        var existing = await FindCustomerId(email, ct);
        if (existing != null) return existing;

        await CustomerLock.WaitAsync(ct);
        try
        {
            existing = await FindCustomerId(email, ct);
            if (existing != null) return existing;

            // The customer may already exist at Stax (e.g. created by staff before the shopper registered).
            var found = (await _stax.FindCustomersByEmail(email, ct))
                .FirstOrDefault(c => StaxGateway.Str(c, "deleted_at") == null);
            string id;
            if (found.ValueKind == JsonValueKind.Object)
            {
                id = StaxGateway.Str(found, "id")!;
            }
            else
            {
                var local = email.Split('@')[0];
                var created = await _stax.CreateCustomer(email, local, "Shopper", null, ct);
                id = StaxGateway.Str(created, "id")
                     ?? throw new AppException(HttpStatusCode.BadGateway, "Stax did not return the new customer.");
            }

            _db.StaxCustomerLinks.Add(new StaxCustomerLink { BuyerEmail = email, StaxCustomerId = id });
            await _db.SaveChangesAsync(ct);
            return id;
        }
        finally
        {
            CustomerLock.Release();
        }
    }

    /// <summary>Every Stax customer record that ever belonged to a shopper: the linked one plus any found by e-mail.</summary>
    public async Task<List<string>> AllCustomerIds(string email, CancellationToken ct = default)
    {
        var ids = new List<string>();
        var link = await FindCustomerId(email, ct);
        if (link != null) ids.Add(link);
        foreach (var c in await _stax.FindCustomersByEmail(email, ct))
        {
            var id = StaxGateway.Str(c, "id");
            if (id != null && !ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);
        }
        return ids;
    }
}
