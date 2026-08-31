using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The outcome of an operator resend: the notification it produced, and whether
/// the idempotency key had already been used (in which case nothing new was sent).</summary>
public record ResendResult(OrderNotification Notification, bool IdempotentReplay);

/// <summary>
/// Orchestrates order notifications: composing messages, handing them to the messaging
/// provider and recording what became of each one. Messaging failures are contained here
/// and never surface to the caller's operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>Cancels every provider-queued message still pending for a contact number.</summary>
    Task CancelPendingMessagesToNumberAsync(string buyerId, string phoneNumber, CancellationToken ct = default);

    /// <summary>Refreshes a notification's delivery outcome from the provider (best-effort).</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Returns the notifications for an order owned by the buyer, refreshed from the provider
    /// (best-effort). Null when the order does not exist or belongs to someone else.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. A repeat under an idempotency key
    /// that was already used returns the original attempt without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(OrderNotification source, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. Returns false when the
    /// provider could not confirm the redaction, in which case nothing is changed locally.
    /// </summary>
    Task<bool> RedactContentAsync(OrderNotification notification, CancellationToken ct = default);
}
