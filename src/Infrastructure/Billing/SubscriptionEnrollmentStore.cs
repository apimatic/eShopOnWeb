using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Persists <see cref="SubscriptionEnrollment"/> rows. Every method leaves nothing tracked behind, so a refused
/// claim cannot be re-sent by a later SaveChanges in the same request scope.
/// </summary>
public class SubscriptionEnrollmentStore
{
    private readonly CatalogContext _db;

    public SubscriptionEnrollmentStore(CatalogContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Inserts the claim. Returns false when the shopper already holds one: the primary key on
    /// <see cref="SubscriptionEnrollment.BuyerId"/> refuses the second insert.
    /// </summary>
    public async Task<bool> TryClaimAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken)
    {
        var entry = _db.SubscriptionEnrollments.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Relational providers: primary-key violation.
            entry.State = EntityState.Detached;
            return false;
        }
        catch (ArgumentException)
        {
            // The in-memory provider reports a duplicate key as ArgumentException ("An item with the same key
            // has already been added"); only treat it as a refused claim when the competing row is really there.
            entry.State = EntityState.Detached;
            if (await ExistsAsync(claim.BuyerId, cancellationToken))
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

    private Task<bool> ExistsAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.SubscriptionEnrollments.AsNoTracking().AnyAsync(e => e.BuyerId == buyerId, cancellationToken);

    public Task<SubscriptionEnrollment?> FindAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.SubscriptionEnrollments.AsNoTracking().FirstOrDefaultAsync(e => e.BuyerId == buyerId, cancellationToken);

    /// <summary>
    /// Applies <paramref name="update"/> to the stored claim, but only while it is still the claim identified by
    /// <paramref name="subscriptionReference"/> (a released and re-taken claim is left alone).
    /// </summary>
    public async Task<SubscriptionEnrollment?> UpdateAsync(string buyerId, string subscriptionReference,
        Action<SubscriptionEnrollment> update, CancellationToken cancellationToken)
    {
        var stored = await _db.SubscriptionEnrollments
            .FirstOrDefaultAsync(e => e.BuyerId == buyerId && e.SubscriptionReference == subscriptionReference, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        try
        {
            update(stored);
            await _db.SaveChangesAsync(cancellationToken);
            return stored;
        }
        finally
        {
            _db.Entry(stored).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Releases the claim identified by <paramref name="subscriptionReference"/> so the shopper may subscribe again.
    /// </summary>
    public async Task ReleaseAsync(string buyerId, string subscriptionReference, CancellationToken cancellationToken)
    {
        var stored = await _db.SubscriptionEnrollments
            .FirstOrDefaultAsync(e => e.BuyerId == buyerId && e.SubscriptionReference == subscriptionReference, cancellationToken);
        if (stored is null)
        {
            return;
        }

        _db.SubscriptionEnrollments.Remove(stored);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released by a concurrent request.
        }
        finally
        {
            _db.Entry(stored).State = EntityState.Detached;
        }
    }
}
