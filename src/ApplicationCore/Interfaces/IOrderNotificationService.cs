using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendNotificationStatus
{
    Sent,
    Duplicate,
    NotFound,
    DestinationUnavailable,
    ContentUnavailable,
    ProviderError
}

public record ResendNotificationResult(ResendNotificationStatus Status, OrderNotification? Notification);

public enum DeleteNotificationContentStatus
{
    Success,
    NotFound,
    ProviderError
}

/// <summary>
/// Orchestrates shopper notifications as orders move. Notification failures never
/// fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper.
    /// Repeating a call under the same idempotency key returns the originally produced
    /// notification without sending again.
    /// </summary>
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content both locally and at the provider, keeping the
    /// record that the message was sent and its outcome.
    /// </summary>
    Task<DeleteNotificationContentStatus> DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default);
}
