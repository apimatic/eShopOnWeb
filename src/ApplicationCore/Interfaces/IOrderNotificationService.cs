using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the shopper SMS notifications as their order moves. A message that cannot be
/// sent never fails the underlying order operation; a shopper with no number on file
/// is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order is cancelled and calls off any not-yet-sent follow-up.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>The caller's own orders, each with its notifications.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Notifications for one of the caller's own orders, with provider state refreshed best-effort.</summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct = default);
}
