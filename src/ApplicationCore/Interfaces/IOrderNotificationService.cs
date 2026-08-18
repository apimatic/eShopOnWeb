using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order lifecycle and the SMS notifications that go out as an order moves.
/// A message that cannot be sent never fails the underlying operation: the order is still
/// placed, dispatched or cancelled and the notification simply records the failure.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from catalog items, reusing the app's Order /
    /// OrderItem model, and tells the shopper their order was placed. Returns the created order.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order dispatched: tells the shopper it is on its way and queues a follow-up
    /// "how was delivery?" message with the provider for a few days later. Returns null if the
    /// order does not exist.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order: tells the shopper and calls off any not-yet-sent follow-up so it never
    /// reaches them. Returns null if the order does not exist.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message a notification represents. The <paramref name="idempotencyKey"/>
    /// de-duplicates: repeating under the same key returns the notification the first attempt
    /// produced and sends nothing further; a fresh key produces a new message. Returns null if the
    /// source notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a notification's message content at the provider. The record that a message was
    /// sent, and what became of it, survive. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> RedactNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the notifications for an order the caller owns, refreshing each one's delivery outcome
    /// from the provider first (there is no callback URL, so state is obtained by asking the provider).
    /// Returns null if the order does not exist or does not belong to <paramref name="ownerId"/>.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOwnedOrderNotificationsAsync(int orderId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Returns the caller's own orders, each with its notifications and their current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured number in a date range
    /// and lines them up against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>One requested order line: a catalog item id and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with its notifications, for the shopper's my-orders view.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>The reconciliation of the provider's record against eShop's for a date range.</summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public int ProviderMessageCount { get; init; }
    public int EShopMessageCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider did not return for the range.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages present on both sides.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = new List<ReconciliationEntry>();
}

/// <summary>A single line of the reconciliation report. Notification/order ids are set on the eShop side.</summary>
public record ReconciliationEntry(string? Sid, string? Status, DateTimeOffset? DateSent, int? NotificationId, int? OrderId);
