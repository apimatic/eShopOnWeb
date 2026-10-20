using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class EfBillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _dbContext;

    public EfBillingClaimStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryAddAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default) where TClaim : BillingClaim
    {
        var entry = _dbContext.Set<TClaim>().Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The store refused the insert: SQL Server raises DbUpdateException for a duplicate key, the in-memory
            // provider an ArgumentException. Only a duplicate key means "someone else holds the claim"; any other
            // failure (lost connection, a constraint we did not expect) must not be mistaken for one.
            entry.State = EntityState.Detached;
            var exists = await _dbContext.Set<TClaim>().AsNoTracking()
                .AnyAsync(c => c.Reference == claim.Reference, cancellationToken);
            if (exists)
            {
                return false;
            }
            throw;
        }
    }

    public Task<TClaim?> GetAsync<TClaim>(string reference, CancellationToken cancellationToken = default) where TClaim : BillingClaim
    {
        return _dbContext.Set<TClaim>().FirstOrDefaultAsync(c => c.Reference == reference, cancellationToken);
    }

    public async Task UpdateAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default) where TClaim : BillingClaim
    {
        _dbContext.Set<TClaim>().Update(claim);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default) where TClaim : BillingClaim
    {
        _dbContext.Set<TClaim>().Remove(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released by another request.
            _dbContext.Entry(claim).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<SubscriptionClaim>> ListUnsettledSubscriptionClaimsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SubscriptionClaims
            .Where(c => c.BuyerId == buyerId && c.State != BillingClaimState.Completed)
            .ToListAsync(cancellationToken);
    }
}
