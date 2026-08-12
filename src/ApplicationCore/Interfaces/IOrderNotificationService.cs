using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>The result of a resend: the identifier of the message the resend produced, and whether it was a fresh send.</summary>
public record ResendResult(int NotificationId, bool WasAlreadySent);

/// <summary>One message lined up between the provider's records and eShop's, for the reconciliation report.</summary>
public record ReconciliationEntry(string? ProviderMessageSid, int? NotificationId, string Status, string? MaskedTo, DateTimeOffset? DateSent);

/// <summary>A reconciliation of the provider's record of messages against eShop's, over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>
/// Drives the order notification flows: placing an order and telling the shopper, dispatch and its
/// scheduled follow-up, cancellation and calling that follow-up off, and the operator actions
/// (resend, content disposal, reconciliation). A message that cannot be sent never fails the
/// underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the buyer from catalog items and tells them it was placed. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks the order dispatched, tells the shopper, and queues the delivery follow-up with the provider. False if the order does not exist.</summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order, tells the shopper, and calls off any follow-up not yet sent. False if the order does not exist.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same idempotency key does
    /// not send again — it returns the message the first call produced. Null if the notification does not exist.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content at the provider and locally. False if the notification does not exist.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>The buyer's orders, refreshed with where each order's notifications got to.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>The buyer's own notifications for an order (with delivery status refreshed). Null if the order is not the buyer's / does not exist.</summary>
    Task<IReadOnlyList<OrderNotification>?> GetOwnedOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>The notifications for an order, whoever owns it. Used by the order-scoped view.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's record of messages sent from the configured number against eShop's, over the range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
