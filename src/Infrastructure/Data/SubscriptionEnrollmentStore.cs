using System;
using System.Linq;
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

    public Task<SubscriptionEnrollment?> GetAsync(string buyerId, CancellationToken cancellationToken = default) =>
        _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(e => e.BuyerId == buyerId, cancellationToken);

    public async Task<bool> TryClaimAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (_dbContext.SubscriptionEnrollments.Local.Any(e => e.BuyerId == enrollment.BuyerId))
        {
            return false;
        }

        _dbContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        // SQL Server refuses a duplicate primary key with a DbUpdateException; the in-memory provider
        // (UseOnlyInMemoryDatabase) with the ArgumentException of its key dictionary.
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            // Stop tracking the rejected row so this context stays usable.
            _dbContext.Entry(enrollment).State = EntityState.Detached;

            // The store refused the insert; it was a second claim if the buyer's enrollment is there now.
            if (await _dbContext.SubscriptionEnrollments.AsNoTracking()
                    .AnyAsync(e => e.BuyerId == enrollment.BuyerId, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public async Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionEnrollments.Remove(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            return false;
        }
    }
}
