using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One requested line of an order: a catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications sent about it, with their current outcomes.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<SmsNotification> Notifications);

/// <summary>
/// Drives an order through its lifecycle and keeps the shopper informed by text as it moves.
/// A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog items for the buyer, then tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a delivery
    /// follow-up with the provider for a few days later. Returns null if the order does not exist.
    /// </summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not yet gone out.
    /// Returns null if the order does not exist.
    /// </summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The buyer's own orders, each with where its notifications got to.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one of the buyer's orders, refreshed against the provider.
    /// Returns null if the order does not exist or does not belong to the buyer.
    /// </summary>
    Task<OrderWithNotifications?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
