using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends SMS notifications as orders move. Messaging must never fail the underlying
/// order operation: provider failures are recorded on the notification, not thrown.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Send a fresh copy of an earlier message under a caller-supplied idempotency key.</summary>
    Task<OrderNotification> SendResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's text, locally and at the provider.</summary>
    Task RedactContentAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Refresh a notification's delivery outcome by asking the provider.</summary>
    Task SyncStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}
