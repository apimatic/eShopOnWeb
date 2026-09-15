using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderSmsNotifier
{
    Task NotifyAsync(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        CancellationToken cancellationToken,
        DateTimeOffset? scheduleAt = null);

    Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken);

    Task RefreshAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken);
}
