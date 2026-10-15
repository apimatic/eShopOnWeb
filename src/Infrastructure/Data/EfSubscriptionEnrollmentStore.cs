using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class EfSubscriptionEnrollmentStore : ISubscriptionEnrollmentStore
{
    private readonly CatalogContext _dbContext;

    public EfSubscriptionEnrollmentStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SubscriptionEnrollment?> TryClaimAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken = default)
    {
        // Fast path for the common repeat request. This read is not the guard — the primary-key insert below is.
        var current = await GetAsync(claim.ClaimKey, cancellationToken);
        if (current is not null)
        {
            return current;
        }

        _dbContext.SubscriptionEnrollments.Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _dbContext.Entry(claim).State = EntityState.Detached;
            var existing = await GetAsync(claim.ClaimKey, cancellationToken);
            if (existing is null)
            {
                // No row holds the key, so this was not a duplicate-key refusal (or the holder was released meanwhile).
                throw;
            }
            return existing;
        }
    }

    public async Task<SubscriptionEnrollment?> GetAsync(string claimKey, CancellationToken cancellationToken = default)
    {
        var tracked = _dbContext.SubscriptionEnrollments.Local.FindEntry(claimKey);
        if (tracked is not null)
        {
            tracked.State = EntityState.Detached;
        }
        return await _dbContext.SubscriptionEnrollments
            .AsTracking()
            .FirstOrDefaultAsync(e => e.ClaimKey == claimKey, cancellationToken);
    }

    public async Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.Entry(enrollment);
        if (entry.State == EntityState.Detached)
        {
            _dbContext.SubscriptionEnrollments.Update(enrollment);
        }
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionEnrollments.Remove(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released or changed by another caller — nothing left for us to release.
            _dbContext.Entry(enrollment).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// SQL Server reports a primary-key violation as <see cref="DbUpdateException"/>; the in-memory provider used
    /// in development raises <see cref="ArgumentException"/> from its key map. Both mean "already claimed".
    /// </summary>
    private static bool IsDuplicateKey(Exception ex) =>
        ex is ArgumentException
        || (ex is DbUpdateException and not DbUpdateConcurrencyException);
}
