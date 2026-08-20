using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITrackedNotificationStore
{
    Task<OrderNotification?> GetTrackedAsync(int notificationId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task SaveRedactionAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}
