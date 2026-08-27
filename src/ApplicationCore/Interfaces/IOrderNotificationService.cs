using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the shopper-facing SMS notifications tied to order lifecycle events.
/// Messaging is best-effort by contract: a message that cannot be sent must
/// never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Cancels any provider-scheduled messages still addressed to a contact number.</summary>
    Task CancelScheduledForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default);

    /// <summary>Best-effort refresh of a notification's delivery outcome from the provider.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key
    /// guarantees a repeated request under the same key sends nothing twice.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider and locally. Returns false if the notification does not exist.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
}

public enum ResendOutcome
{
    Completed = 0,
    AlreadyProcessed = 1,
    NotFound = 2,
    DestinationNoLongerRegistered = 3
}

public record ResendResult(ResendOutcome Outcome, OrderNotification? Notification);
