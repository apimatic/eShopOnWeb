using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);
    Task CancelPendingFollowUpsForOrderAsync(int orderId, CancellationToken cancellationToken);
    Task CancelPendingFollowUpsForContactAsync(int contactNumberId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> GetCurrentNotificationsAsync(int orderId,
        CancellationToken cancellationToken);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
}

public sealed class FollowUpCancellationException : Exception
{
    public FollowUpCancellationException(int notificationId)
        : base($"Scheduled notification {notificationId} could not be safely cancelled.") { }
}

public sealed class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message) { }
}
