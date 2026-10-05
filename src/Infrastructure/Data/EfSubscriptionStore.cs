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
/// Claim store on <see cref="CatalogContext"/>. A claim is a primary-key insert: SQL Server refuses a duplicate key
/// with a <see cref="DbUpdateException"/>, the in-memory provider with an <see cref="ArgumentException"/> or
/// <see cref="DbUpdateException"/> depending on version — both are caught here and reported as "not claimed".
/// Takeovers and updates are guarded by the <c>ClaimToken</c> concurrency token.
/// </summary>
public class EfSubscriptionStore : ISubscriptionStore
{
    private readonly CatalogContext _db;

    public EfSubscriptionStore(CatalogContext db)
    {
        _db = db;
    }

    public async Task<BillingCustomer?> GetCustomerAsync(string userId, CancellationToken cancellationToken) =>
        await _db.BillingCustomers.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<bool> TryClaimCustomerAsync(BillingCustomer claim, CancellationToken cancellationToken) =>
        TryInsertAsync(claim, cancellationToken);

    public Task<bool> TryRenewCustomerClaimAsync(BillingCustomer claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        claim.Renew(now);
        return TrySaveAsync(claim, cancellationToken);
    }

    public Task<bool> SaveCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken) =>
        TrySaveAsync(customer, cancellationToken);

    public Task ReleaseCustomerClaimAsync(BillingCustomer claim, CancellationToken cancellationToken) =>
        TryDeleteAsync(claim, cancellationToken);

    public async Task<SubscriptionEnrollment?> GetEnrollmentAsync(string id, CancellationToken cancellationToken) =>
        await _db.SubscriptionEnrollments.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string userId, CancellationToken cancellationToken) =>
        await _db.SubscriptionEnrollments.Where(e => e.UserId == userId).ToListAsync(cancellationToken);

    public Task<bool> TryClaimEnrollmentAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken) =>
        TryInsertAsync(claim, cancellationToken);

    public Task<bool> TryRenewEnrollmentClaimAsync(SubscriptionEnrollment claim, DateTimeOffset now, CancellationToken cancellationToken)
    {
        claim.Renew(now);
        return TrySaveAsync(claim, cancellationToken);
    }

    public Task<bool> SaveEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken) =>
        TrySaveAsync(enrollment, cancellationToken);

    public Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken) =>
        TryDeleteAsync(enrollment, cancellationToken);

    private async Task<bool> TryInsertAsync<T>(T claim, CancellationToken cancellationToken) where T : class
    {
        try
        {
            _db.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            // The store refused the second claim for the same key.
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
        catch (InvalidOperationException)
        {
            // The same key is already tracked by this context (claimed earlier in the same request).
            if (_db.Entry(claim).State != EntityState.Detached)
            {
                _db.Entry(claim).State = EntityState.Detached;
            }
            return false;
        }
    }

    private async Task<bool> TrySaveAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request changed (or released) the claim first.
            _db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    private async Task TryDeleteAsync<T>(T entity, CancellationToken cancellationToken) where T : class
    {
        _db.Remove(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released or taken over by another request.
            _db.Entry(entity).State = EntityState.Detached;
        }
    }
}
