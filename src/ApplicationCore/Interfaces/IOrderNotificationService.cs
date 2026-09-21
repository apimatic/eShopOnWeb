using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item id and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places orders (reusing the existing Order aggregate) and drives the SMS notifications as an order
/// moves. A messaging failure never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for <paramref name="buyerId"/> from catalog items, tell the shopper it was placed,
    /// and return the new order's id. Throws when a catalog item id is unknown or the order is empty.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Operator: mark the order dispatched, tell the shopper, and queue the delivery survey with the provider.</summary>
    Task<OrderTransitionResult> DispatchAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel the order, tell the shopper, and call off any not-yet-sent delivery survey.</summary>
    Task<OrderTransitionResult> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>The caller's orders, each showing where its notifications got to (statuses refreshed from the provider).</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// The notifications for an order (shopper-scoped: only the owner's order). Returns null when the order
    /// is not the caller's / has no tracked record. Statuses are refreshed from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Operator: re-send a message that did not reach the shopper. Repeating under the same idempotency key
    /// returns the earlier result without sending again; a fresh key sends.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Operator: dispose of a message's content — redact it at the provider and locally — while keeping the
    /// fact it was sent and what became of it. Returns false when the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Operator: reconcile the provider's record for a date range against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
