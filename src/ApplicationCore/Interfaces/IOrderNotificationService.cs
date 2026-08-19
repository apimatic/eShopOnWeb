using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items and drives the messages that go out as an order moves.
/// Reuses the existing <see cref="Order"/>/<see cref="OrderItem"/> model.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper and tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a
    /// follow-up with the provider for a few days later. Returns null if the order is absent.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not yet
    /// gone out. Returns null if the order is absent.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each with the notifications raised for it (status refreshed).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications raised for one of the caller's orders (status refreshed).
    /// Returns null if the order does not exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested catalog line on a new order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications raised for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);
