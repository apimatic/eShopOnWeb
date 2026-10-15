using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ClaimStore(CatalogContext db)
{
    public async Task<bool> TryClaimAsync(string id, CancellationToken ct)
    {
        var claim = new BillingClaim { Id = id };
        db.BillingClaims.Add(claim);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            db.Entry(claim).State = EntityState.Detached;
            // Only treat a save failure as a duplicate when the conflicting key exists.
            if (await db.BillingClaims.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return false;
            throw;
        }
    }

    public async Task CompleteAsync(string id, int providerId, CancellationToken ct)
    {
        var claim = await db.BillingClaims.FindAsync(new object[] { id }, ct);
        if (claim == null) return;
        claim.ProviderId = providerId;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseAsync(string id, CancellationToken ct)
    {
        var claim = await db.BillingClaims.FindAsync(new object[] { id }, ct);
        if (claim == null) return;
        db.BillingClaims.Remove(claim);
        await db.SaveChangesAsync(ct);
    }
}
