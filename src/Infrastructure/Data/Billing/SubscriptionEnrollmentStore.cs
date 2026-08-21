using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Data.Billing;

public sealed class SubscriptionEnrollmentStore
{
    private readonly CatalogContext _context;

    public SubscriptionEnrollmentStore(CatalogContext context)
    {
        _context = context;
    }

    public Task<SubscriptionEnrollment?> FindAsync(
        string userKey,
        string productHandle,
        CancellationToken cancellationToken)
    {
        return _context.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserKey == userKey && x.ProductHandle == productHandle,
            cancellationToken);
    }

    public async Task<bool> TryAddAsync(
        SubscriptionEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        _context.SubscriptionEnrollments.Add(enrollment);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);
}
