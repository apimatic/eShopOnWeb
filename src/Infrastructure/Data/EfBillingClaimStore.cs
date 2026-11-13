using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// <see cref="IBillingClaimStore"/> on <see cref="CatalogContext"/>. Entities are detached after every call so a
/// refused write never lingers in the change tracker to be re-sent by a later <c>SaveChanges</c>.
/// </summary>
public class EfBillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _db;

    public EfBillingClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public async Task<bool> TryInsertAsync(BillingClaim claim, CancellationToken cancellationToken = default)
    {
        var entry = _db.BillingClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKeyCandidate(ex))
        {
            entry.State = EntityState.Detached;
            // The store refused the insert. It is a duplicate claim only if a row now exists under this key;
            // anything else (store unavailable, schema missing) is a real failure and must surface.
            if (await FindAsync(claim.Key, cancellationToken) is not null)
            {
                return false;
            }
            throw;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task<BillingClaim?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        _db.BillingClaims.AsNoTracking().SingleOrDefaultAsync(c => c.Key == key, cancellationToken);

    public async Task<bool> TryUpdateAsync(BillingClaim claim, Action<BillingClaim> change, CancellationToken cancellationToken = default)
    {
        // Attach snapshots the loaded Version as the original value; the change rotates it, so SaveChanges
        // succeeds only if nobody else rotated it in between.
        var entry = _db.BillingClaims.Attach(claim);
        try
        {
            change(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<bool> TryReleaseAsync(BillingClaim claim, CancellationToken cancellationToken = default)
    {
        var entry = _db.BillingClaims.Attach(claim);
        try
        {
            entry.State = EntityState.Deleted;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    // SQL Server reports a duplicate primary key as DbUpdateException; the EF InMemory provider throws
    // ArgumentException from its key map (proven by BillingClaimStoreTest). Either is only a *candidate*:
    // TryInsertAsync confirms the duplicate by finding the existing row, and rethrows otherwise.
    private static bool IsDuplicateKeyCandidate(Exception ex) =>
        ex is DbUpdateException or ArgumentException;
}
