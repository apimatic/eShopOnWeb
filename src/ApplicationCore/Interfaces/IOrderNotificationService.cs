using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws for messaging failures.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up
    /// with the provider for a few days later. Never throws for messaging failures.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order was cancelled and cancels any follow-up that has
    /// not yet gone out. Never throws for messaging failures.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>The notifications for an order owned by the buyer, with each non-terminal
    /// message's outcome refreshed from the provider. Null when the order does not exist
    /// or does not belong to the buyer.</summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct = default);

    /// <summary>Refreshes the outcome of the buyer's recent non-terminal notifications from
    /// the provider (best effort, bounded).</summary>
    Task SyncRecentStatusesAsync(string buyerId, CancellationToken ct = default);

    /// <summary>All notifications for the buyer's orders, after a bounded best-effort
    /// refresh of recent non-terminal outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Refreshes one notification's outcome from the provider (best effort:
    /// provider failures leave the last known state in place).</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default);

    /// <summary>Sends a fresh copy of a notification's message and records it under the
    /// caller's idempotency key. Throws SmsProviderException when the provider rejects the send.</summary>
    Task<OrderNotification> ResendAsync(OrderNotification source, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Erases the message text at the provider and locally. The record of the send
    /// and its outcome survive. Throws SmsProviderException when the provider redaction fails.</summary>
    Task RedactContentAsync(OrderNotification notification, CancellationToken ct = default);
}
