using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Insert-or-fail claims for billing writes, kept in the application's own database. A claim is written
/// before the billing system is called; the store's primary key refuses a second claim for the same key, so a
/// double-submit or a concurrent request never reaches the billing system twice.
/// </summary>
public class EnrollmentStore
{
    private readonly CatalogContext _db;

    public EnrollmentStore(CatalogContext db)
    {
        _db = db;
    }

    public sealed record ClaimResult<T>(bool Claimed, T? Entity) where T : class;

    /// <summary>
    /// Inserts <paramref name="candidate"/>. Returns <c>Claimed = true</c> when this caller now owns the claim, or
    /// the row that already holds it (null if that row vanished in between).
    /// </summary>
    public async Task<ClaimResult<SubscriptionEnrollment>> TryClaimEnrollmentAsync(SubscriptionEnrollment candidate,
        CancellationToken cancellationToken)
    {
        _db.SubscriptionEnrollments.Add(candidate);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimResult<SubscriptionEnrollment>(true, candidate);
        }
        catch (Exception ex) when (IsPossibleDuplicateKey(ex))
        {
            _db.Entry(candidate).State = EntityState.Detached;
            var existing = await _db.SubscriptionEnrollments
                .FirstOrDefaultAsync(e => e.UserId == candidate.UserId && e.ProductFamilyHandle == candidate.ProductFamilyHandle,
                    cancellationToken);
            if (existing is null && ex is DbUpdateException)
            {
                throw;   // not a key conflict — a genuine save failure
            }
            return new ClaimResult<SubscriptionEnrollment>(false, existing);
        }
    }

    public async Task<ClaimResult<BillingCustomer>> TryClaimCustomerAsync(BillingCustomer candidate,
        CancellationToken cancellationToken)
    {
        _db.BillingCustomers.Add(candidate);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimResult<BillingCustomer>(true, candidate);
        }
        catch (Exception ex) when (IsPossibleDuplicateKey(ex))
        {
            _db.Entry(candidate).State = EntityState.Detached;
            var existing = await _db.BillingCustomers.FirstOrDefaultAsync(c => c.UserId == candidate.UserId, cancellationToken);
            if (existing is null && ex is DbUpdateException)
            {
                throw;   // not a key conflict — a genuine save failure
            }
            return new ClaimResult<BillingCustomer>(false, existing);
        }
    }

    public Task<BillingCustomer?> FindCustomerAsync(string userId, CancellationToken cancellationToken) =>
        _db.BillingCustomers.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<SubscriptionEnrollment?> FindEnrollmentAsync(string userId, string productFamilyHandle,
        CancellationToken cancellationToken) =>
        _db.SubscriptionEnrollments.FirstOrDefaultAsync(e => e.UserId == userId && e.ProductFamilyHandle == productFamilyHandle,
            cancellationToken);

    /// <summary>Enrollments whose outcome is not settled: <c>Unknown</c>, or <c>Pending</c> claims older than <paramref name="pendingStaleBefore"/>.</summary>
    public async Task<List<SubscriptionEnrollment>> ListUnsettledAsync(string? userId, DateTimeOffset pendingStaleBefore,
        int max, CancellationToken cancellationToken)
    {
        var query = _db.SubscriptionEnrollments
            .Where(e => e.State == EnrollmentState.Unknown || e.State == EnrollmentState.Pending);
        if (userId is not null)
        {
            query = query.Where(e => e.UserId == userId);
        }

        // DateTimeOffset comparisons are filtered client-side: not every provider translates them.
        var candidates = await query.Take(max).ToListAsync(cancellationToken);
        return candidates
            .Where(e => e.State == EnrollmentState.Unknown || e.ClaimedAt < pendingStaleBefore)
            .ToList();
    }

    /// <summary>Persists changes made to claimed/loaded entities. Outcomes are always saved, even if the caller went away.</summary>
    public Task SaveAsync() => _db.SaveChangesAsync(CancellationToken.None);

    /// <summary>Removes a claim the billing system refused (or that never landed) so the user can try again.</summary>
    public async Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment)
    {
        _db.SubscriptionEnrollments.Remove(enrollment);
        await SaveIgnoringVanishedRowsAsync(enrollment);
    }

    public async Task ReleaseCustomerAsync(BillingCustomer customer)
    {
        _db.BillingCustomers.Remove(customer);
        await SaveIgnoringVanishedRowsAsync(customer);
    }

    private async Task SaveIgnoringVanishedRowsAsync(object entity)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed by a concurrent settlement — the claim is gone either way.
            _db.Entry(entity).State = EntityState.Detached;
        }
    }

    /// <summary>
    /// The store refusing a second claim. A relational primary-key violation surfaces as <see cref="DbUpdateException"/>
    /// (confirmed by finding the existing row; otherwise rethrown); the EF in-memory provider raises
    /// <see cref="ArgumentException"/> ("An item with the same key has already been added"); a key already tracked by
    /// this context raises <see cref="InvalidOperationException"/>. Provider exception types are deliberately not
    /// referenced so the check stays provider-agnostic.
    /// </summary>
    private static bool IsPossibleDuplicateKey(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => false,
        DbUpdateException => true,
        ArgumentException { Message: var m } when m.Contains("same key", StringComparison.OrdinalIgnoreCase) => true,
        InvalidOperationException { Message: var m } when m.Contains("same key value", StringComparison.OrdinalIgnoreCase) => true,
        _ => false
    };
}
