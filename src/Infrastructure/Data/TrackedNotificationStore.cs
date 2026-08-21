using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class TrackedNotificationStore : ITrackedNotificationStore
{
    private readonly CatalogContext _db;

    public TrackedNotificationStore(CatalogContext db)
    {
        _db = db;
    }

    public Task<OrderNotification?> GetAsync(int id, CancellationToken cancellationToken = default)
        => _db.OrderNotifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
