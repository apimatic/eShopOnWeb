using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog items and notifies the shopper. A failed notification never fails the order.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks the order dispatched, notifies the shopper and queues a provider-held follow-up. Null when the order does not exist.</summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order, notifies the shopper and calls off any not-yet-sent follow-up. Null when the order does not exist.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>What was sent for an order, with each message's outcome refreshed from the provider (best effort).</summary>
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Re-sends a message that did not reach the shopper. A repeated idempotency key replays the first result without sending again. Null when the notification does not exist.</summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider and locally; the record of the message survives. False when the notification does not exist.</summary>
    Task<bool> DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lines the provider's record of messages for a range up against what the app believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed record OrderItemRequest(int CatalogItemId, int Units);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly);

public sealed record ReconciliationEntry(
    string? MessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? LocalStatus,
    bool StatusAgreement);
