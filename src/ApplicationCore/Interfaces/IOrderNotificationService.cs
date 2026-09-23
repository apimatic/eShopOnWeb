using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates order placement and the SMS notifications that go out as an order moves. A message that
/// cannot be sent is recorded but never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the shopper from catalog items, then notify them it was placed. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderRequestItem> items, CancellationToken ct);

    /// <summary>Operator action: mark dispatched, notify the shopper, and queue the delivery follow-up.
    /// Returns false if the order was not in a state that transitions to dispatched.</summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancel the order, notify the shopper, and call off any not-yet-sent follow-up.
    /// Returns false if the order was already cancelled.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Shopper-scoped: the caller's orders, each with where its notifications got to (provider state refreshed).</summary>
    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Shopper-scoped: notifications for one of the caller's orders (provider state refreshed).
    /// Throws <see cref="Exceptions.OrderNotFoundException"/> if the order is not the caller's.</summary>
    Task<IReadOnlyList<NotificationView>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct);

    /// <summary>Operator action: re-send a message that did not reach the shopper. A repeated
    /// <paramref name="idempotencyKey"/> returns the first result without sending again.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Operator action: dispose the message content at the provider and locally; the fact and outcome survive.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Operator action: reconcile the provider's messages (from the configured number) against eShop's records over a window.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
