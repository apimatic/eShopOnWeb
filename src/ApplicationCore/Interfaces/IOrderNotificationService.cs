using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the existing order/order-item model) and drives the SMS notifications that go
/// out as an order moves, plus the operator actions over those notifications. A message that cannot be
/// sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog items, then tells them it was placed. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken);

    /// <summary>Operator: marks the order dispatched, tells the shopper, and queues the delivery follow-up for a few days later.</summary>
    Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator: cancels the order, tells the shopper, and calls off the not-yet-sent follow-up.</summary>
    Task<OrderActionResult> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>The caller's own orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>What was sent for the caller's own order, and what became of each message. Null if the order is not theirs.</summary>
    Task<IReadOnlyList<NotificationSummary>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);

    /// <summary>Operator: re-sends a message that did not reach the shopper, deduplicated by the caller's idempotency key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Operator: disposes of a message's content at the provider (redaction). Returns false if not found.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>Operator: reconciles the provider's record of this application's messages against eShop's, over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
