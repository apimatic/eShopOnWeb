using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the shopper SMS notifications as an order moves and keeps the local
/// notification records in step with the provider. Messaging problems never
/// fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is on its way and queues the delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is cancelled and calls off any follow-up that has not yet gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. A repeated call under
    /// an already-used idempotency key returns the original resend record
    /// without sending again.
    /// </summary>
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Pulls the current delivery outcome from the provider into the local record.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}

public record ResendNotificationResult(
    ResendNotificationOutcome Outcome,
    OrderNotification? Notification);

public enum ResendNotificationOutcome
{
    Sent,
    AlreadyProcessed,
    NotFound,
    ContentRedacted,
    NoContactNumber
}
