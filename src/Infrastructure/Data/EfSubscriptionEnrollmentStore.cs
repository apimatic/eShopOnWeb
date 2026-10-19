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
    private readonly TimeProvider _clock;

    public EfSubscriptionEnrollmentStore(CatalogContext dbContext, TimeProvider clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<SubscriptionEnrollment?> TryClaimAsync(SubscriptionEnrollment candidate, CancellationToken cancellationToken)
    {
        _dbContext.SubscriptionEnrollments.Add(candidate);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // The store refused the second claim for this buyer: hand back the claim that won.
            _dbContext.Entry(candidate).State = EntityState.Detached;
            var existing = await _dbContext.SubscriptionEnrollments
                .SingleOrDefaultAsync(e => e.BuyerId == candidate.BuyerId, cancellationToken);
            return existing ?? throw new InvalidOperationException("The enrollment claim was refused but no existing claim was found.", ex);
        }
    }

    public async Task<bool> TryTakeOverAsync(SubscriptionEnrollment existing, string planHandle, CancellationToken cancellationToken)
    {
        existing.TakeOver(planHandle, _clock.GetUtcNow());
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request took the claim over (or released it) first.
            _dbContext.Entry(existing).State = EntityState.Detached;
            return false;
        }
    }

    public Task<SubscriptionEnrollment?> GetAsync(string buyerId, CancellationToken cancellationToken) =>
        _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(e => e.BuyerId == buyerId, cancellationToken);

    public async Task SaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        if (_dbContext.Entry(enrollment).State == EntityState.Detached)
        {
            _dbContext.SubscriptionEnrollments.Update(enrollment);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        _dbContext.SubscriptionEnrollments.Remove(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released or taken over by another request; nothing left to release.
            _dbContext.Entry(enrollment).State = EntityState.Detached;
        }
    }

    // A relational provider raises DbUpdateException for a primary-key violation; the in-memory
    // provider raises ArgumentException ("An item with the same key has already been added").
    private static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException and not DbUpdateConcurrencyException
        || ex is ArgumentException && ex.Message.Contains("same key", StringComparison.Ordinal);
}
