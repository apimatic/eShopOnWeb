using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and records SMS notifications as orders move. A notification failure must
/// never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper.
    /// Returns the new notification produced by the resend.
    /// </summary>
    Task<OrderNotification> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the provider-owned delivery outcome of a notification.</summary>
    Task RefreshProviderStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}
