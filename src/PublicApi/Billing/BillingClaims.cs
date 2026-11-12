using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Claims taken before a billing write. The BillingClaims primary key makes the store refuse a second claim
/// for the same key. Uses its own scope so a refused insert never taints the request's own context.
/// </summary>
public class BillingClaims
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopes;

    public BillingClaims(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    /// <summary>Takes the claim or throws 409. Dispose the result to release it.</summary>
    public async Task<IAsyncDisposable> ClaimAsync(string key, string conflictMessage)
    {
        key = key.Length > 256 ? key[..256] : key;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
                db.BillingClaims.Add(new BillingClaim { Key = key, ClaimedAtUtc = DateTime.UtcNow });
                try
                {
                    await db.SaveChangesAsync();
                    return new Lease(_scopes, key);
                }
                catch (DbUpdateException)
                {
                    // The store refused the claim: someone holds it (or held it and died).
                }
            }

            using (var checkScope = _scopes.CreateScope())
            {
                var check = checkScope.ServiceProvider.GetRequiredService<CatalogContext>();
                var existing = await check.BillingClaims.FirstOrDefaultAsync(c => c.Key == key);
                if (existing == null) continue;
                if (DateTime.UtcNow - existing.ClaimedAtUtc <= StaleAfter)
                    throw new BillingException(409, conflictMessage);
                check.BillingClaims.Remove(existing);
                try { await check.SaveChangesAsync(); } catch (DbUpdateException) { }
            }
        }
        throw new BillingException(409, conflictMessage);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly string _key;

        public Lease(IServiceScopeFactory scopes, string key)
        {
            _scopes = scopes;
            _key = key;
        }

        public async ValueTask DisposeAsync()
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var existing = await db.BillingClaims.FirstOrDefaultAsync(c => c.Key == _key);
            if (existing != null)
            {
                db.BillingClaims.Remove(existing);
                try { await db.SaveChangesAsync(); } catch (DbUpdateException) { }
            }
        }
    }
}
