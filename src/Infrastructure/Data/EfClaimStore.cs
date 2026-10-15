using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claims refused by the database: a second insert of the same primary key fails in the store
/// (SQL Server and EF InMemory alike), and updates carry a concurrency token so only one writer
/// can take a claim over.
/// </summary>
public class EfClaimStore<T> : IClaimStore<T> where T : class, IAggregateRoot
{
    private readonly CatalogContext _dbContext;

    public EfClaimStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryAddAsync(T claim, CancellationToken cancellationToken)
    {
        var keyValues = KeyValuesOf(claim);
        try
        {
            _dbContext.Set<T>().Add(claim);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            // The store refused the insert. Only call it a lost claim when the competing row is really there.
            _dbContext.Entry(claim).State = EntityState.Detached;
            if (await ExistsInStoreAsync(keyValues, cancellationToken))
            {
                return false;
            }
            throw;
        }
        catch (System.InvalidOperationException) when (IsTrackedElsewhere(claim, keyValues))
        {
            // This context already tracks a claim with the same key: it exists.
            return false;
        }
        catch (System.ArgumentException)
        {
            // EF InMemory reports a duplicate key on save as an ArgumentException from its row table.
            _dbContext.Entry(claim).State = EntityState.Detached;
            if (await ExistsInStoreAsync(keyValues, cancellationToken))
            {
                return false;
            }
            throw;
        }
    }

    public async Task<T?> FindAsync(object[] key, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Set<T>().FindAsync(key, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        // FindAsync may return a copy tracked earlier in this request; read the store's current state.
        var entry = _dbContext.Entry(entity);
        await entry.ReloadAsync(cancellationToken);
        return entry.State == EntityState.Detached ? null : entity;
    }

    public async Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken)
    {
        return await SpecificationEvaluator.Default.GetQuery(_dbContext.Set<T>().AsQueryable(), specification)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryUpdateAsync(T claim, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer changed or removed the claim since this one read it.
            await _dbContext.Entry(claim).ReloadAsync(CancellationToken.None);
            return false;
        }
    }

    public async Task RemoveAsync(T claim, CancellationToken cancellationToken)
    {
        _dbContext.Set<T>().Remove(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed or taken over by another writer: nothing left for this one to release.
            _dbContext.Entry(claim).State = EntityState.Detached;
        }
    }

    private object[] KeyValuesOf(T claim)
    {
        var key = _dbContext.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        var entry = _dbContext.Entry(claim);
        return key.Properties.Select(p => entry.Property(p.Name).CurrentValue!).ToArray();
    }

    private bool IsTrackedElsewhere(T claim, object[] keyValues)
    {
        var key = _dbContext.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!;
        return _dbContext.ChangeTracker.Entries<T>().Any(e =>
            !ReferenceEquals(e.Entity, claim) &&
            key.Properties.Select(p => e.Property(p.Name).CurrentValue).SequenceEqual(keyValues));
    }

    private async Task<bool> ExistsInStoreAsync(object[] keyValues, CancellationToken cancellationToken)
    {
        // The refused claim is detached by now, so this answers from another tracked claim or the store.
        return await _dbContext.Set<T>().FindAsync(keyValues, cancellationToken) is not null;
    }
}
