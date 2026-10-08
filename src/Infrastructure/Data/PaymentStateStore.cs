using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class PaymentStateStore : IPaymentStateStore
{
    private readonly CatalogContext _dbContext;

    public PaymentStateStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryClaimAsync<T>(T claim, CancellationToken cancellationToken) where T : class, IAggregateRoot
    {
        var entry = _dbContext.Set<T>().Add(claim);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        // Relational providers report a primary-key violation as DbUpdateException; the in-memory provider
        // raises ArgumentException ("same key has already been added").
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            var keyValues = entry.Metadata.FindPrimaryKey()!.Properties
                .Select(p => entry.Property(p.Name).CurrentValue)
                .ToArray();
            entry.State = EntityState.Detached;

            // Only a key that really is taken is a lost claim; anything else is a genuine failure.
            var existing = await _dbContext.Set<T>().FindAsync(keyValues, cancellationToken);
            if (existing is not null)
            {
                return false;
            }
            throw;
        }
    }

    public async Task<bool> TrySaveOrderAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _dbContext.Entry(order).ReloadAsync(cancellationToken);
            return false;
        }
    }
}
