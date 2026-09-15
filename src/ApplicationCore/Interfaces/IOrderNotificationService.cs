using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives an order through its life — placed, dispatched, cancelled — and keeps the shopper informed by
/// SMS at each step. A message that cannot be sent never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog item ids and quantities, reusing the existing
    /// order/order-item model, then tells the shopper it was placed. Returns the created order.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched (operator action), tells the shopper it is on its way, and queues a
    /// "how was delivery?" follow-up with the provider for a few days later. Returns false if no such
    /// order was placed through the API.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order (operator action), calls off any not-yet-sent follow-up with the provider, and
    /// tells the shopper. Returns false if no such order was placed through the API.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, each with its state and where its notifications got to.</summary>
    Task<IReadOnlyList<OrderDeliveryView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one of the caller's own orders, each refreshed against the provider.
    /// Returns null when the order is not the caller's or was not placed through the API.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string ownerId,
        CancellationToken cancellationToken = default);
}
