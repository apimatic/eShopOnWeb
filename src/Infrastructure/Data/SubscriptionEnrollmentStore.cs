using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class SubscriptionEnrollmentStore : ISubscriptionEnrollmentStore
{
    private readonly CatalogContext _dbContext;

    public SubscriptionEnrollmentStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SubscriptionEnrollment?> GetAsync(string userReference, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserReference == userReference, cancellationToken);
    }

    public async Task<bool> TryClaimAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            entry.State = EntityState.Detached;
            return true;
        }
        // SQL Server reports a primary-key violation as DbUpdateException; the in-memory provider
        // reports the same violation as ArgumentException ("same key has already been added").
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, Guid expectedClaimToken, CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.SubscriptionEnrollments.Attach(enrollment);
        entry.State = EntityState.Modified;
        // Only succeeds if the stored token is still the one this caller holds.
        entry.Property(e => e.ClaimToken).OriginalValue = expectedClaimToken;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.SubscriptionEnrollments.Attach(enrollment);
        entry.State = EntityState.Deleted;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released or taken over by another caller — nothing left to release.
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }
}
