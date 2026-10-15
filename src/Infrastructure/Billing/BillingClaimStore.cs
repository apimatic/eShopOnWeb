using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

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
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(claim).State = EntityState.Detached;
            return false;
        }
        catch (ArgumentException) when (db.Database.IsInMemory())
        {
            db.Entry(claim).State = EntityState.Detached;
            // Do not disguise an unrelated ArgumentException as a duplicate claim.
            if (await db.BillingClaims.AsNoTracking().AnyAsync(x => x.Id == claim.Id, ct)) return false;
            throw;
        }
    }

    public async Task CompleteAsync(BillingClaim claim, int providerId, CancellationToken ct)
    {
        claim.ProviderId = providerId;
        claim.State = "Complete";
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task BeginDispatchAsync(BillingClaim claim, CancellationToken ct)
    {
        claim.State = "Dispatching";
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task UnknownAsync(BillingClaim claim, CancellationToken ct)
    {
        claim.State = "Unknown";
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseAsync(BillingClaim claim, CancellationToken ct)
    {
        db.BillingClaims.Remove(claim);
        await db.SaveChangesAsync(ct);
    }
}
