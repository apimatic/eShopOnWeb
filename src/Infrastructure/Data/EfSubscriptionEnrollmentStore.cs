using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Subscription claims in <see cref="CatalogContext"/>. Entities are never left tracked, so each operation
/// works from the values it is handed and the store — not the change tracker — decides conflicts.
/// </summary>
public class EfSubscriptionEnrollmentStore : ISubscriptionEnrollmentStore
{
    private readonly CatalogContext _db;

    public EfSubscriptionEnrollmentStore(CatalogContext db)
    {
        _db = db;
    }

    public Task<SubscriptionEnrollment?> FindAsync(string userName, CancellationToken cancellationToken) =>
        _db.SubscriptionEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserName == userName, cancellationToken);

    public async Task<bool> TryClaimAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken)
    {
        var entry = _db.SubscriptionEnrollments.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        // SQL Server raises DbUpdateException on the primary-key violation; the EF in-memory provider raises
        // ArgumentException ("An item with the same key has already been added").
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            entry.State = EntityState.Detached;
            // The insert was refused: it was a duplicate claim exactly when a row with this key now exists.
            if (await _db.SubscriptionEnrollments.AsNoTracking()
                    .AnyAsync(e => e.UserName == claim.UserName, cancellationToken))
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

    public async Task<bool> TrySaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var entry = _db.Entry(enrollment);
        entry.State = EntityState.Modified;
        var version = entry.Property(e => e.Version);
        var expected = enrollment.Version;
        version.OriginalValue = expected;
        version.CurrentValue = Guid.NewGuid();
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            version.CurrentValue = expected;
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var entry = _db.Entry(enrollment);
        entry.State = EntityState.Deleted;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed or taken over by another request; nothing of ours left to release.
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }
}
