using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class BillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _dbContext;

    public BillingClaimStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BillingClaim?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        var tracked = _dbContext.ChangeTracker.Entries<BillingClaim>();
        foreach (var entry in tracked)
        {
            if (entry.Entity.Key == key)
            {
                // Always read the store's current state, not what this scope last saw.
                await entry.ReloadAsync(cancellationToken);
                return entry.State == EntityState.Detached ? null : entry.Entity;
            }
        }

        return await _dbContext.BillingClaims.FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
    }

    public async Task<bool> TryClaimAsync(BillingClaim claim, CancellationToken cancellationToken = default)
    {
        _dbContext.BillingClaims.Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Primary-key violation: another request already holds the claim.
            _dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
        catch (ArgumentException)
        {
            // Some providers surface a duplicate key as ArgumentException ("same key has already been added").
            _dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryTakeOverAsync(BillingClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        claim.TakeOver(now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else re-armed or settled the claim between our read and this write.
            await ReloadOrDetachAsync(claim, cancellationToken);
            return false;
        }
    }

    public async Task CompleteAsync(BillingClaim claim, int providerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        claim.Complete(providerId, now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The provider's answer is the truth regardless of who holds the claim: overwrite.
            await ReloadOrDetachAsync(claim, cancellationToken);
            if (_dbContext.Entry(claim).State == EntityState.Detached)
            {
                var replacement = new BillingClaim(claim.Key, claim.Kind, now);
                replacement.Complete(providerId, now);
                _dbContext.BillingClaims.Add(replacement);
            }
            else
            {
                claim.Complete(providerId, now);
            }
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkUnknownAsync(BillingClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        claim.MarkUnknown(now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another caller owns the claim now; it will settle the outcome.
            await ReloadOrDetachAsync(claim, cancellationToken);
        }
    }

    public async Task ReleaseAsync(BillingClaim claim, CancellationToken cancellationToken = default)
    {
        _dbContext.BillingClaims.Remove(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed or taken over by another caller: nothing of ours to release.
            await ReloadOrDetachAsync(claim, cancellationToken);
        }
    }

    private async Task ReloadOrDetachAsync(BillingClaim claim, CancellationToken cancellationToken)
    {
        var entry = _dbContext.Entry(claim);
        await entry.ReloadAsync(cancellationToken);
    }
}
