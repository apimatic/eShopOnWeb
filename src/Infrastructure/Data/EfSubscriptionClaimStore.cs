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
/// Subscription claims kept in the application's own store. A claim is a plain insert keyed on the
/// primary key, so the database (and the in-memory provider) refuses the second one.
/// </summary>
public class EfSubscriptionClaimStore : ISubscriptionClaimStore
{
    private readonly CatalogContext _dbContext;

    public EfSubscriptionClaimStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryClaimEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        // Cheap early answer for the common replay; the primary-key insert below is what actually refuses a racing claim.
        if (await _dbContext.SubscriptionEnrollments.AnyAsync(e => e.BuyerId == enrollment.BuyerId && e.PlanHandle == enrollment.PlanHandle, cancellationToken))
        {
            return false;
        }

        return await TryInsertAsync(enrollment, cancellationToken);
    }

    public Task<SubscriptionEnrollment?> GetEnrollmentAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default) =>
        _dbContext.SubscriptionEnrollments
            .FirstOrDefaultAsync(e => e.BuyerId == buyerId && e.PlanHandle == planHandle, cancellationToken);

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default) =>
        await _dbContext.SubscriptionEnrollments
            .Where(e => e.BuyerId == buyerId)
            .ToListAsync(cancellationToken);

    public Task UpdateEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default) =>
        DeleteAsync(enrollment, cancellationToken);

    public async Task<bool> TryClaimCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default)
    {
        if (await _dbContext.BillingCustomers.AnyAsync(c => c.BuyerId == customer.BuyerId, cancellationToken))
        {
            return false;
        }

        return await TryInsertAsync(customer, cancellationToken);
    }

    public Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default) =>
        _dbContext.BillingCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, cancellationToken);

    public Task UpdateCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default) =>
        DeleteAsync(customer, cancellationToken);

    private async Task<bool> TryInsertAsync<T>(T claim, CancellationToken cancellationToken) where T : class
    {
        _dbContext.Set<T>().Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // Primary-key violation: another request holds this claim. Stop tracking the refused row so it is not re-sent.
            _dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    // A relational store reports the violation as DbUpdateException; the in-memory provider refuses the
    // duplicate key with an ArgumentException from its key table.
    private bool IsDuplicateKey(Exception exception) =>
        exception is DbUpdateException and not DbUpdateConcurrencyException
        || (exception is ArgumentException && _dbContext.Database.IsInMemory());

    private async Task DeleteAsync<T>(T claim, CancellationToken cancellationToken) where T : class
    {
        _dbContext.Set<T>().Remove(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed by a concurrent request.
            _dbContext.Entry(claim).State = EntityState.Detached;
        }
    }
}
