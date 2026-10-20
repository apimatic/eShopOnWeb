using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class SubscriptionEnrollmentStore : ISubscriptionEnrollmentStore
{
    private readonly CatalogContext _db;

    public SubscriptionEnrollmentStore(CatalogContext db) => _db = db;

    public async Task<EnrollmentClaimResult> TryClaimAsync(SubscriptionEnrollment candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            _db.SubscriptionEnrollments.Add(candidate);
            await _db.SaveChangesAsync(cancellationToken);
            return new EnrollmentClaimResult(true, candidate);
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException || ex is ArgumentException)
        {
            // The primary key refused the second claim (SQL Server: DbUpdateException; in-memory: ArgumentException from the key table; same-context: InvalidOperationException).
            _db.Entry(candidate).State = EntityState.Detached;
            var existing = await _db.SubscriptionEnrollments.FirstOrDefaultAsync(e => e.UserId == candidate.UserId, cancellationToken);
            if (existing is null)
            {
                throw; // not a duplicate: a genuine store failure
            }
            return new EnrollmentClaimResult(false, existing);
        }
    }

    public Task<SubscriptionEnrollment?> GetAsync(string userId, CancellationToken cancellationToken = default)
        => _db.SubscriptionEnrollments.FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);

    public async Task SaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        _db.SubscriptionEnrollments.Remove(enrollment);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
