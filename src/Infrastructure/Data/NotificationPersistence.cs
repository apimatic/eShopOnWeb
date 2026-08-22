using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class NotificationPersistence : INotificationPersistence
{
    private readonly CatalogContext _db;

    public NotificationPersistence(CatalogContext db)
    {
        _db = db;
    }

    public async Task MarkContentRedactedAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications
            .AsTracking()
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken)
            ?? throw new NotificationNotFoundException(notificationId);

        notification.MarkContentRedacted();

        var entry = _db.Entry(notification);
        entry.Property(n => n.ContentRedacted).IsModified = true;
        entry.Property(n => n.Body).IsModified = true;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
