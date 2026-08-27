using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task SendOrderEventAsync(Order order, NotificationKind kind, DateTimeOffset? scheduledFor, CancellationToken cancellationToken);
    Task CancelScheduledFollowUpsAsync(int orderId, int? contactNumberId, CancellationToken cancellationToken);
    Task CancelOutstandingScheduledMessagesAsync(CancellationToken cancellationToken);
    Task RefreshAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactAsync(OrderNotification notification, CancellationToken cancellationToken);
}
