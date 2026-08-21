using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionRecordStore : ISubscriptionRecordStore
{
    private readonly CatalogContext _context;

    public SubscriptionRecordStore(CatalogContext context) => _context = context;

    public Task<SubscriptionRecord?> GetAsync(string userId, string productHandle, CancellationToken cancellationToken = default) =>
        _context.SubscriptionRecords.SingleOrDefaultAsync(
            record => record.UserId == userId && record.ProductHandle == productHandle,
            cancellationToken);

    public async Task<IReadOnlyList<SubscriptionRecord>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
        await _context.SubscriptionRecords.Where(record => record.UserId == userId).ToListAsync(cancellationToken);

    public async Task<bool> TryAddAsync(SubscriptionRecord record, CancellationToken cancellationToken = default)
    {
        _context.SubscriptionRecords.Add(record);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _context.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public async Task SaveAsync(SubscriptionRecord record, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(record).State == EntityState.Detached) _context.SubscriptionRecords.Update(record);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
