using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class OrderNotificationCommands : IOrderNotificationCommands
{
    private readonly CatalogContext _dbContext;

    public OrderNotificationCommands(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task PersistDisposalAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.OrderNotifications.FindAsync(new object[] { notificationId }, cancellationToken);
        if (notification == null)
        {
            throw new KeyNotFoundException("Notification was not found.");
        }

        notification.MarkContentDisposed();
        var entry = _dbContext.Entry(notification);
        entry.Property(n => n.ContentDisposed).IsModified = true;
        entry.Property(n => n.Body).IsModified = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
