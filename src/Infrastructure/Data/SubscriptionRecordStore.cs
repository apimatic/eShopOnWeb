using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public sealed class SubscriptionRecordStore : ISubscriptionRecordStore
{
    private readonly CatalogContext _context;

    public SubscriptionRecordStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task SynchronizeAsync(
        string userId,
        SubscriptionDetails subscription,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.SubscriptionRecords.SingleOrDefaultAsync(
            record => record.SubscriptionReference == subscription.Reference,
            cancellationToken);

        if (existing is not null)
        {
            existing.Synchronize(subscription.CustomerId, subscription.Id, DateTimeOffset.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var record = new SubscriptionRecord(
            userId,
            subscription.ProductHandle,
            subscription.Reference,
            subscription.CustomerId,
            subscription.Id,
            DateTimeOffset.UtcNow);
        _context.SubscriptionRecords.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request may have synchronized the same deterministic reference.
            _context.Entry(record).State = EntityState.Detached;
            if (!await _context.SubscriptionRecords.AsNoTracking().AnyAsync(
                    candidate => candidate.SubscriptionReference == subscription.Reference,
                    cancellationToken))
            {
                throw;
            }
        }
    }
}
