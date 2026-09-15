using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationSender
{
    Task<OrderNotification?> TryNotifyAsync(
        Order order,
        NotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        int? sourceNotificationId = null,
        CancellationToken cancellationToken = default);

    Task SyncFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}
