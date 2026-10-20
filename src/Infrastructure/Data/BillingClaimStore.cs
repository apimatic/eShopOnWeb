using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// EF Core implementation of <see cref="IBillingClaimStore"/> over <see cref="CatalogContext"/>.
/// A claim is an INSERT keyed by the customer reference: the store's primary-key check is what refuses the
/// second caller, so two racing requests can never both reach the billing provider.
/// </summary>
public class BillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _context;

    public BillingClaimStore(CatalogContext context)
    {
        _context = context;
    }

    public Task<bool> TryClaimCustomerAsync(BillingCustomerLink claim, CancellationToken cancellationToken = default) =>
        TryInsertAsync(claim, claim.CustomerReference, cancellationToken);

    public Task<BillingCustomerLink?> FindCustomerLinkAsync(string customerReference, CancellationToken cancellationToken = default) =>
        _context.BillingCustomerLinks.FirstOrDefaultAsync(l => l.CustomerReference == customerReference, cancellationToken);

    public Task<bool> UpdateCustomerLinkAsync(BillingCustomerLink link, CancellationToken cancellationToken = default) =>
        SaveGuardedAsync(link, remove: false, cancellationToken);

    public Task<bool> ReleaseCustomerClaimAsync(BillingCustomerLink link, CancellationToken cancellationToken = default) =>
        SaveGuardedAsync(link, remove: true, cancellationToken);

    public Task<bool> TryClaimSubscriptionAsync(SubscriptionClaim claim, CancellationToken cancellationToken = default) =>
        TryInsertAsync(claim, claim.CustomerReference, cancellationToken);

    public Task<SubscriptionClaim?> FindSubscriptionClaimAsync(string customerReference, CancellationToken cancellationToken = default) =>
        _context.SubscriptionClaims.FirstOrDefaultAsync(c => c.CustomerReference == customerReference, cancellationToken);

    public Task<bool> UpdateSubscriptionClaimAsync(SubscriptionClaim claim, CancellationToken cancellationToken = default) =>
        SaveGuardedAsync(claim, remove: false, cancellationToken);

    public Task<bool> ReleaseSubscriptionClaimAsync(SubscriptionClaim claim, CancellationToken cancellationToken = default) =>
        SaveGuardedAsync(claim, remove: true, cancellationToken);

    private async Task<bool> TryInsertAsync<T>(T entity, string key, CancellationToken cancellationToken) where T : class
    {
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>? entry = null;
        try
        {
            entry = _context.Set<T>().Add(entity);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        // SQL Server reports a primary-key violation as DbUpdateException; the EF in-memory provider reports a
        // duplicate key as an ArgumentException/InvalidOperationException; a key already tracked by this context
        // is an InvalidOperationException from Add. In every case confirm the key is really taken before
        // treating the failure as "already claimed".
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException or InvalidOperationException)
        {
            if (entry != null)
            {
                entry.State = EntityState.Detached;
            }

            var taken = await _context.Set<T>()
                .AsNoTracking()
                .AnyAsync(e => EF.Property<string>(e, "CustomerReference") == key, cancellationToken);
            if (taken)
            {
                return false;
            }

            throw;
        }
    }

    private async Task<bool> SaveGuardedAsync<T>(T entity, bool remove, CancellationToken cancellationToken) where T : class
    {
        var entry = _context.Entry(entity);
        if (remove)
        {
            _context.Set<T>().Remove(entity);
        }
        else if (entry.State == EntityState.Detached)
        {
            _context.Set<T>().Update(entity);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            if (remove)
            {
                entry.State = EntityState.Detached;
            }
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another caller changed or removed the claim first (the Version concurrency token no longer matches).
            entry.State = EntityState.Detached;
            return false;
        }
    }
}
