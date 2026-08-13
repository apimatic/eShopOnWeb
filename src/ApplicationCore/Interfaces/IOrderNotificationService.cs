using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items and drives the messages that go out as an order moves. A message
/// that cannot be sent never fails the underlying order operation; a shopper with no number on file is
/// simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper and tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: marks the order dispatched, tells the shopper it is on its way, and queues the
    /// "how did delivery go?" follow-up with the provider for a few days later. Returns false if the
    /// order does not exist.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancels the order, calls off any not-yet-sent follow-up, and tells the shopper.
    /// Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each with the notifications sent for it and where they got to.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications sent for one of the caller's orders and what became of each. Null when the
    /// order does not exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications sent for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);
