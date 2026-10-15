using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// EF Core implementation of <see cref="ISubscriptionClaimStore"/> over <see cref="CatalogContext"/>.
/// Claims are plain inserts: the primary key refuses a second claim on every provider, including in-memory.
/// </summary>
public class SubscriptionClaimStore : ISubscriptionClaimStore
{
    private readonly CatalogContext _dbContext;

    public SubscriptionClaimStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default) =>
        _dbContext.BillingCustomers.SingleOrDefaultAsync(c => c.BuyerId == buyerId, cancellationToken);

    public async Task<BillingCustomer?> TryClaimCustomerAsync(BillingCustomer claim, CancellationToken cancellationToken = default)
    {
        _dbContext.BillingCustomers.Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (IsPossibleDuplicateKey(ex))
        {
            _dbContext.Entry(claim).State = EntityState.Detached;
            var existing = await GetCustomerAsync(claim.BuyerId, cancellationToken);
            if (existing is null)
            {
                throw; // not a duplicate: the insert failed for another reason
            }
            return existing;
        }
    }

    public async Task SaveCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Entry(customer).State == EntityState.Detached)
        {
            _dbContext.BillingCustomers.Update(customer);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default)
    {
        _dbContext.BillingCustomers.Remove(customer);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released by someone else.
            _dbContext.Entry(customer).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default) =>
        await _dbContext.SubscriptionEnrollments.Where(e => e.BuyerId == buyerId).ToListAsync(cancellationToken);

    public async Task<SubscriptionEnrollment?> TryClaimEnrollmentAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionEnrollments.Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (IsPossibleDuplicateKey(ex))
        {
            _dbContext.Entry(claim).State = EntityState.Detached;
            var existing = await _dbContext.SubscriptionEnrollments
                .SingleOrDefaultAsync(e => e.BuyerId == claim.BuyerId && e.PlanHandle == claim.PlanHandle, cancellationToken);
            if (existing is null)
            {
                throw; // not a duplicate: the insert failed for another reason
            }
            return existing;
        }
    }

    public async Task<bool> TrySaveEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Entry(enrollment).State == EntityState.Detached)
        {
            _dbContext.SubscriptionEnrollments.Update(enrollment);
        }
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

    public async Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        _dbContext.SubscriptionEnrollments.Remove(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed or released by another request; that request now owns the row.
            _dbContext.Entry(enrollment).State = EntityState.Detached;
        }
    }

    // Relational providers report a key violation as DbUpdateException; the in-memory provider
    // raises InvalidOperationException/ArgumentException. Callers confirm by re-reading the key.
    private static bool IsPossibleDuplicateKey(Exception ex) =>
        ex is DbUpdateException or InvalidOperationException or ArgumentException;
}
