using System;
using System.Linq;
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

    public async Task<bool> TryClaimAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        if (_dbContext.SubscriptionEnrollments.Local.Any(e => e.EnrollmentKey == enrollment.EnrollmentKey))
        {
            return false;
        }

        _dbContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // Another request holds the claim. Detach ours so the existing row can be read back.
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            return false;
        }
    }

    public Task<SubscriptionEnrollment?> GetAsync(string enrollmentKey, CancellationToken cancellationToken) =>
        _dbContext.SubscriptionEnrollments.FirstOrDefaultAsync(e => e.EnrollmentKey == enrollmentKey, cancellationToken);

    public async Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(enrollment).ReloadAsync(cancellationToken);
            return false;
        }
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
            // Already released or taken over by someone else.
            _dbContext.Entry(enrollment).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// A primary-key violation: <see cref="DbUpdateException"/> from relational providers; the EF in-memory
    /// provider surfaces it as the <see cref="ArgumentException"/> of its backing dictionary
    /// ("An item with the same key has already been added").
    /// </summary>
    private static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException and not DbUpdateConcurrencyException
        || ex is ArgumentException && ex.Message.Contains("same key", StringComparison.Ordinal);
}
