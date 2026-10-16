using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
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
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
        {
            db.Entry(claim).State = EntityState.Detached;
            return false;
        }
        catch (ArgumentException) when (db.Database.IsInMemory())
        {
            db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public async Task CompleteAsync(BillingClaim claim, int id, CancellationToken ct)
    {
        claim.ProviderId = id;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseAsync(BillingClaim claim, CancellationToken ct)
    {
        db.BillingClaims.Remove(claim);
        await db.SaveChangesAsync(ct);
    }
}
