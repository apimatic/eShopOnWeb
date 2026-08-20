using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class SubscriptionLinkStore : ISubscriptionLinkStore
{
    private readonly CatalogContext _context;

    public SubscriptionLinkStore(CatalogContext context)
    {
        _context = context;
    }

    public Task<SubscriptionLink?> FindAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        return _context.SubscriptionLinks.SingleOrDefaultAsync(
            link => link.UserId == userId && link.ProductHandle == productHandle,
            cancellationToken);
    }

    public async Task SaveAsync(SubscriptionLink link, CancellationToken cancellationToken)
    {
        if (link.Id == 0)
        {
            _context.SubscriptionLinks.Add(link);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var existing = await FindAsync(link.UserId, link.ProductHandle, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            existing.Refresh(link.MaxioCustomerId, link.MaxioSubscriptionId, link.UpdatedAt);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
