using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class BillingClaimStore(CatalogContext db)
{
    public async Task<bool> TryClaimAsync(BillingClaim claim, CancellationToken ct)
    {
        db.BillingClaims.Add(claim);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is ArgumentException)
        {
            db.Entry(claim).State = EntityState.Detached;
            // Do not turn an unrelated persistence fault into a duplicate.
            if (!await db.BillingClaims.AsNoTracking().AnyAsync(x => x.Id == claim.Id, ct)) throw;
            return false;
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
        db.Remove(claim);
        await db.SaveChangesAsync(ct);
    }
}
