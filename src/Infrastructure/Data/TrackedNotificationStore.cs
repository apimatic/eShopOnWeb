using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class TrackedNotificationStore : ITrackedNotificationStore
{
    private readonly CatalogContext _db;
    private readonly INotificationRedactionState _redactionState;

    public TrackedNotificationStore(CatalogContext db, INotificationRedactionState redactionState)
    {
        _db = db;
        _redactionState = redactionState;
    }

    public Task<OrderNotification?> GetTrackedAsync(int notificationId, CancellationToken cancellationToken = default)
        => _db.OrderNotifications.SingleOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public async Task SaveRedactionAsync(OrderNotification notification, CancellationToken cancellationToken = default)
    {
        notification.RedactContent();
        _redactionState.MarkRedacted(notification.Id);

        try
        {
            var updated = await _db.OrderNotifications
                .Where(n => n.Id == notification.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(n => n.Body, string.Empty)
                        .SetProperty(n => n.ContentRedacted, true),
                    cancellationToken);
            if (updated > 0)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // The in-memory provider cannot translate ExecuteUpdate; fall back to instance update.
        }

        var entry = _db.Entry(notification);
        if (entry.State is not EntityState.Detached and not EntityState.Added)
        {
            entry.State = EntityState.Detached;
        }

        _db.OrderNotifications.Update(notification);
        var updatedEntry = _db.Entry(notification);
        updatedEntry.Property(n => n.Body).CurrentValue = string.Empty;
        updatedEntry.Property(n => n.ContentRedacted).CurrentValue = true;
        updatedEntry.Property(n => n.Body).IsModified = true;
        updatedEntry.Property(n => n.ContentRedacted).IsModified = true;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
