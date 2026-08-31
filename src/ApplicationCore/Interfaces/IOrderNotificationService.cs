using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog items and notifies the shopper. A failed
    /// notification never fails the order.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Marks the order dispatched, notifies the shopper, and queues a delivery
    /// follow-up with the provider for a few days later.</summary>
    Task<Order> DispatchAsync(int orderId, CancellationToken ct = default);

    /// <summary>Cancels the order, notifies the shopper, and calls off any follow-up
    /// that has not yet gone out.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Returns null when the order does not exist or belongs to another shopper.</summary>
    Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct = default);

    /// <summary>Notifications for an order whose ownership the caller has already established.</summary>
    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Re-sends the message of a notification that did not reach the shopper.
    /// A repeated idempotency key replays the original resend without sending again.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Disposes of a message's content at the provider and locally; the record
    /// that a message was sent, and its outcome, survive.</summary>
    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Lines up the provider's record of messages for a date range against
    /// what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
