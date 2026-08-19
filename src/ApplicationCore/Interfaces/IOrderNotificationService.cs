using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives the order-notification flows: placing an order and telling the shopper, marking it
/// dispatched or cancelled and messaging accordingly (including a scheduled follow-up that is
/// called off on cancellation), and the operator actions over the resulting messages. A message
/// that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the owner from catalog items and quantities, reusing the existing order
    /// model, then tells the shopper it was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, ShippingAddress? address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a "how did the
    /// delivery go?" follow-up with the provider for a few days later. Operator action.
    /// </summary>
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not yet gone out.
    /// Operator action.
    /// </summary>
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The owner's orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderSummaryView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications for one of the owner's orders, each with its current delivery outcome.
    /// Returns null if the order does not exist or does not belong to the owner.
    /// </summary>
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper, under a caller-supplied idempotency key.
    /// Repeating the same key returns the message the first attempt produced without sending again.
    /// Returns the id of the notification the re-send produced. Operator action.
    /// </summary>
    Task<int> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content so its text is no longer retrievable from the provider or
    /// here, while the fact it was sent and what became of it survive. Operator action.
    /// </summary>
    Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages from the configured sender over a date range
    /// against what eShop believes it sent. Operator action.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
