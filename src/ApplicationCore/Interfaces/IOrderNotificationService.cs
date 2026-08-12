using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders through the existing order model and drives the SMS notifications that go out as an
/// order moves, plus the operator actions over those messages. Sending an SMS never fails the
/// underlying order operation: a message that cannot go out is recorded as such and the operation
/// still succeeds.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the buyer from catalog items and tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a follow-up with the
    /// provider for a few days later asking how the delivery went. Operator action.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not yet gone out.
    /// Operator action.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's own orders together with the notifications sent for each.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns the notifications for one of the buyer's own orders, with current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// idempotency key returns the message already produced instead of sending another. Operator action.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the shopper's request, at the provider as well as locally,
    /// while the fact it was sent and what became of it survives. Operator action.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages sent from the configured sending number in a
    /// date range against what eShop believes it sent. Operator action.
    /// </summary>
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and a quantity.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications that were sent for it.</summary>
public sealed record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>The result of a reconciliation: messages present in both sides, and the discrepancies either way.</summary>
public sealed record ReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report. Fields present depend on which side(s) knew the message.</summary>
public sealed record ReconciliationEntry(
    string? MessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? LocalStatus,
    DateTimeOffset? DateSent);
