using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// <see cref="IBillingClaimStore"/> over <see cref="CatalogContext"/>. Every operation detaches what it
/// touched, so a refused write never lingers in the scoped context's change tracker.
/// </summary>
public class EfBillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _db;

    public EfBillingClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public async Task<BillingClaimAttempt> TryClaimAsync(BillingClaim candidate, CancellationToken cancellationToken = default)
    {
        // Two passes: if the existing claim is released between our refused insert and our read,
        // the second insert attempt can win it.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var entry = _db.BillingClaims.Add(candidate);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return new BillingClaimAttempt(true, candidate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SQL Server raises DbUpdateException for the primary-key violation; the in-memory provider
                // raises InvalidOperationException or ArgumentException depending on timing. Whatever the type,
                // the claim is someone else's only if the row exists — otherwise the failure is rethrown.
                entry.State = EntityState.Detached;
                var existing = await FindAsync(candidate.Key, cancellationToken);
                if (existing is not null)
                {
                    return new BillingClaimAttempt(false, existing);
                }
                if (attempt == 1)
                {
                    throw;
                }
            }
            finally
            {
                entry.State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Unreachable.");
    }

    public async Task<bool> TryUpdateAsync(BillingClaim claim, Action<BillingClaim> change, CancellationToken cancellationToken = default)
    {
        var originalVersion = claim.Version;
        if (_db.Entry(claim).State == EntityState.Detached)
        {
            _db.BillingClaims.Attach(claim);
        }
        var entry = _db.Entry(claim);

        change(claim);
        entry.Property(c => c.Version).OriginalValue = originalVersion;
        entry.State = EntityState.Modified;
        try
        {
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

    public async Task ReleaseAsync(BillingClaim claim, CancellationToken cancellationToken = default)
    {
        if (_db.Entry(claim).State == EntityState.Detached)
        {
            _db.BillingClaims.Attach(claim);
        }
        var entry = _db.Entry(claim);
        entry.State = EntityState.Deleted;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released or re-armed by someone else: nothing of ours left to delete.
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task<BillingClaim?> FindAsync(string key, CancellationToken cancellationToken = default) =>
        _db.BillingClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
}
