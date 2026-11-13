using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>The result of trying to claim a write: whether this caller holds the claim, and the claim row.</summary>
public readonly record struct ClaimAttempt<T>(bool Acquired, T Claim) where T : BillingClaim;

/// <summary>
/// Claims on billing-provider writes, kept in <see cref="CatalogContext"/> (the store eShopOnWeb already keeps
/// its records in). A claim is an INSERT keyed by the write's deterministic reference: the store refuses the
/// second insert for the same key, which is what stops a double-click before it reaches the provider.
/// </summary>
public class BillingClaimStore
{
    private readonly CatalogContext _db;

    public BillingClaimStore(CatalogContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Inserts <paramref name="candidate"/>. If a claim with the same reference exists, the caller acquires it
    /// only when it is still pending and its lease has expired (an abandoned claim); otherwise the existing
    /// claim is returned with <see cref="ClaimAttempt{T}.Acquired"/> false.
    /// </summary>
    public virtual async Task<ClaimAttempt<T>> TryClaimAsync<T>(T candidate, DateTime nowUtc, TimeSpan lease, CancellationToken cancellationToken)
        where T : BillingClaim
    {
        Exception? lastInsertFailure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // A read first keeps the common sequential repeat (a second click after the first finished) off the
            // duplicate-key path. It is NOT the guard: two racing requests both read nothing, and the primary key
            // on the INSERT below is what refuses the second one.
            var tracked = _db.Set<T>().Local.FirstOrDefault(c => c.Reference == candidate.Reference)
                ?? await _db.Set<T>().SingleOrDefaultAsync(c => c.Reference == candidate.Reference, cancellationToken);
            if (tracked is null)
            {
                _db.Set<T>().Add(candidate);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return new ClaimAttempt<T>(true, candidate);
                }
                catch (Exception ex) when (IsPossibleDuplicateKey(ex))
                {
                    // The store refused our claim. Whether it was a duplicate key is settled by the read below.
                    lastInsertFailure = ex;
                    _db.Entry(candidate).State = EntityState.Detached;
                }
            }

            var existing = tracked ?? await _db.Set<T>().SingleOrDefaultAsync(c => c.Reference == candidate.Reference, cancellationToken);
            if (existing is null)
            {
                continue; // released between our insert and our read (or not a duplicate-key failure): try again
            }

            if (existing.IsLeaseExpired(nowUtc) && await TryRenewAsync(existing, nowUtc, lease, cancellationToken))
            {
                return new ClaimAttempt<T>(true, existing);
            }

            return new ClaimAttempt<T>(false, existing);
        }

        throw new InvalidOperationException("Could not claim billing reference after repeated attempts.", lastInsertFailure);
    }

    public virtual Task<T?> FindAsync<T>(string reference, CancellationToken cancellationToken) where T : BillingClaim
        => _db.Set<T>().SingleOrDefaultAsync(c => c.Reference == reference, cancellationToken);

    public virtual Task<List<SubscriptionEnrollment>> ListPendingEnrollmentsAsync(string customerReference, CancellationToken cancellationToken)
        => _db.SubscriptionEnrollments
            .Where(e => e.CustomerReference == customerReference && e.Status == BillingClaimStatus.Pending)
            .ToListAsync(cancellationToken);

    /// <summary>Extends or takes the lease on a pending claim. False if another request changed it first.</summary>
    public virtual async Task<bool> TryRenewAsync<T>(T claim, DateTime nowUtc, TimeSpan lease, CancellationToken cancellationToken)
        where T : BillingClaim
    {
        claim.Renew(nowUtc, lease);
        return await TrySaveAsync(claim, cancellationToken);
    }

    /// <summary>Records the provider id on the claim. False if another request changed the claim first.</summary>
    public virtual async Task<bool> TryCompleteAsync<T>(T claim, long providerId, DateTime nowUtc, CancellationToken cancellationToken)
        where T : BillingClaim
    {
        claim.Complete(providerId, nowUtc);
        return await TrySaveAsync(claim, cancellationToken);
    }

    /// <summary>
    /// Deletes a claim whose write definitely did not happen, so a later request can try again.
    /// A concurrent change (another request took the claim over) is left alone.
    /// </summary>
    public virtual async Task ReleaseAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim
    {
        _db.Set<T>().Remove(claim);
        await TrySaveAsync(claim, cancellationToken);
    }

    private async Task<bool> TrySaveAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// True when an insert failure may be the store refusing a duplicate primary key: SQL Server reports it as a
    /// <see cref="DbUpdateException"/> (error 2627/2601); the EF in-memory provider raises
    /// <see cref="ArgumentException"/> from its key dictionary (pinned by BillingClaimStoreTests). The caller then
    /// reads the row to confirm. The SQL Server exception type is deliberately not referenced, so this code never
    /// loads Microsoft.Data.SqlClient into an in-memory-only process.
    /// </summary>
    internal static bool IsPossibleDuplicateKey(Exception ex)
        => ex is ArgumentException || (ex is DbUpdateException && ex is not DbUpdateConcurrencyException);
}
