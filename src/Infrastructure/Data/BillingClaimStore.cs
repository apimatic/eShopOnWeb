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

    public async Task<bool> TryInsertAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim
    {
        _dbContext.Set<T>().Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException || (ex is ArgumentException && _dbContext.Database.IsInMemory()))
        {
            // Duplicate primary key: another request already holds the claim for this user.
            // (SQL Server reports it as DbUpdateException; the EF InMemory provider as ArgumentException.)
            _dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public Task<T?> GetAsync<T>(string userKey, CancellationToken cancellationToken) where T : BillingClaim
    {
        return _dbContext.Set<T>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserKey == userKey, cancellationToken);
    }

    public async Task<bool> TryUpdateAsync<T>(T claim, Action<T> mutate, CancellationToken cancellationToken) where T : BillingClaim
    {
        var entry = Track(claim);

        mutate(claim);
        claim.Version = Guid.NewGuid();
        entry.State = EntityState.Modified;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryReleaseAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim
    {
        var entry = Track(claim);

        entry.State = EntityState.Deleted;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            return false;
        }
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> Track<T>(T claim) where T : BillingClaim
    {
        if (_dbContext.Entry(claim).State == EntityState.Detached)
        {
            // Attaching snapshots the Version the claim was read with; SaveChanges checks it.
            _dbContext.Set<T>().Attach(claim);
        }
        return _dbContext.Entry(claim);
    }
}
