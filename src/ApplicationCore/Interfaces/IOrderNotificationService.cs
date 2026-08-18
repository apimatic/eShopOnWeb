using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the shopper-facing and operator-facing order-notification flows on top of the domain
/// repositories and the provider-agnostic <see cref="ISmsGateway"/>. A message that cannot be sent never
/// fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    // --- Flow 1: the shopper's contact number (shopper-scoped) ---

    /// <summary>Register a mobile number for a shopper after the provider confirms it is a usable destination.</summary>
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawPhoneNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove one of the shopper's numbers. Returns false if it is not theirs / does not exist.</summary>
    Task<bool> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct = default);

    // --- Flow 2: messages as the order moves ---

    /// <summary>Place an order from catalog items for a shopper and tell them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken ct = default);

    /// <summary>Operator: mark an order dispatched, tell the shopper, and queue a delivery follow-up for later.</summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel an order, tell the shopper, and call off any follow-up not yet sent.</summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>A shopper's orders, each with its notifications' current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>The notifications for one order, scoped to the caller who owns it (null if not theirs).</summary>
    Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken ct = default);

    // --- Flow 3: what the operator can do about it ---

    /// <summary>Operator: re-send a message that did not reach the shopper, idempotent on the supplied key.</summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Operator: dispose of a message's content at the provider and here. Returns false if the notification is unknown.</summary>
    Task<bool> RedactNotificationContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Operator: reconcile the provider's own record of this application's messages against eShop's, over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}

/// <summary>An order paired with its notifications, for the "my orders" view.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);
