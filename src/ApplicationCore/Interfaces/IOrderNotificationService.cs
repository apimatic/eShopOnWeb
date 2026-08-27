using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends SMS notifications as orders progress. Notification failures never fail the
/// underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same
    /// idempotency key returns the original resend without sending again.
    /// </summary>
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text both locally and at the provider, keeping the
    /// record that the message was sent and its outcome.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Cancels any not-yet-sent messages to a contact number (used when the number is removed).</summary>
    Task SuppressPendingMessagesToAsync(ContactNumber contactNumber, CancellationToken cancellationToken = default);

    /// <summary>Best-effort refresh of a notification's delivery outcome from the provider.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}

public enum ResendNotificationStatus
{
    Resent,
    AlreadyProcessed,
    NotFound,
    NotResendable,
    ContentRedacted
}

public class ResendNotificationResult
{
    public ResendNotificationStatus Status { get; set; }
    public OrderNotification? Notification { get; set; }
}
