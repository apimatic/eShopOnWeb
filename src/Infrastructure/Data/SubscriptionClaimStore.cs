using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claims kept in the catalog database. A claim is an insert on the entity's primary key, which every provider this
/// app runs on (SQL Server and the EF in-memory store) refuses for a duplicate key — that refusal is what stops the
/// second of two concurrent requests.
/// </summary>
public class SubscriptionClaimStore : ISubscriptionClaimStore
{
    private readonly CatalogContext _db;

    public SubscriptionClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public Task<bool> TryClaimCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) =>
        TryInsertAsync(customer, cancellationToken);

    public Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default) =>
        _db.BillingCustomers.AsNoTracking().FirstOrDefaultAsync(c => c.BuyerId == buyerId, cancellationToken);

    public Task UpdateCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) =>
        SaveAsync(customer, EntityState.Modified, cancellationToken);

    public Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) =>
        DeleteAsync(customer, cancellationToken);

    public Task<bool> TryClaimSubscriptionAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        TryInsertAsync(enrollment, cancellationToken);

    public Task<SubscriptionEnrollment?> GetEnrollmentAsync(string claimKey, CancellationToken cancellationToken = default) =>
        _db.SubscriptionEnrollments.AsNoTracking().FirstOrDefaultAsync(e => e.ClaimKey == claimKey, cancellationToken);

    public Task<int> CountEndedEnrollmentsAsync(string buyerId, string productFamilyHandle, CancellationToken cancellationToken = default) =>
        _db.SubscriptionEnrollments.AsNoTracking()
            .CountAsync(e => e.BuyerId == buyerId
                             && e.ProductFamilyHandle == productFamilyHandle
                             && e.Status == EnrollmentStatus.Ended, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, string productFamilyHandle, CancellationToken cancellationToken = default) =>
        await _db.SubscriptionEnrollments.AsNoTracking()
            .Where(e => e.BuyerId == buyerId && e.ProductFamilyHandle == productFamilyHandle)
            .OrderBy(e => e.Generation)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListUnsettledEnrollmentsAsync(DateTimeOffset pendingClaimedBefore, int max, CancellationToken cancellationToken = default)
    {
        // DateTimeOffset comparisons are not translated by every provider; filter by status in the store, by age here.
        var candidates = await _db.SubscriptionEnrollments.AsNoTracking()
            .Where(e => e.Status == EnrollmentStatus.Unknown || e.Status == EnrollmentStatus.Pending)
            .ToListAsync(cancellationToken);
        return candidates
            .Where(e => e.Status == EnrollmentStatus.Unknown || e.ClaimedAt < pendingClaimedBefore)
            .OrderBy(e => e.UpdatedAt)
            .Take(max)
            .ToList();
    }

    public Task UpdateEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        SaveAsync(enrollment, EntityState.Modified, cancellationToken);

    public Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        DeleteAsync(enrollment, cancellationToken);

    private async Task<bool> TryInsertAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        Detach(entity);
        var entry = _db.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException and not DbUpdateConcurrencyException || ex is ArgumentException)
        {
            // Forget our insert so later saves in this scope don't replay it.
            entry.State = EntityState.Detached;

            // SQL Server reports a duplicate key as DbUpdateException (2627/2601); the EF in-memory store raises
            // ArgumentException. Either way the insert was refused; it is a lost claim only if the key is now taken.
            if (await KeyExistsAsync(entity, cancellationToken)) return false;
            throw;
        }
        catch
        {
            entry.State = EntityState.Detached;
            throw;
        }
    }

    private async Task<bool> KeyExistsAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        var key = _db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        var keyValues = key.Properties.Select(p => p.PropertyInfo!.GetValue(entity)).ToArray();
        var existing = await _db.FindAsync<T>(keyValues, cancellationToken);
        if (existing is null) return false;
        _db.Entry(existing).State = EntityState.Detached;
        return true;
    }

    private async Task SaveAsync<T>(T entity, EntityState state, CancellationToken cancellationToken) where T : class
    {
        Detach(entity);
        var entry = _db.Entry(entity);
        entry.State = state;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task DeleteAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        try
        {
            await SaveAsync(entity, EntityState.Deleted, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released by another request — the outcome we wanted.
        }
    }

    /// <summary>Stops tracking any other instance with the same key, so the given instance can be attached.</summary>
    private void Detach<T>(T entity) where T : class
    {
        var key = _db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        var keyValues = key.Properties.Select(p => p.PropertyInfo!.GetValue(entity)).ToArray();
        foreach (var tracked in _db.ChangeTracker.Entries<T>().ToList())
        {
            if (ReferenceEquals(tracked.Entity, entity)) continue;
            var trackedValues = key.Properties.Select(p => tracked.Property(p.Name).CurrentValue).ToArray();
            if (keyValues.SequenceEqual(trackedValues)) tracked.State = EntityState.Detached;
        }
    }
}
