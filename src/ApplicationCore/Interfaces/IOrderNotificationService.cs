using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and tracks the SMS notifications that go out as an order moves.
/// Notification failures never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws for messaging failures.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues the delivery follow-up with the
    /// provider for a few days later. Never throws for messaging failures.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up that has not
    /// yet gone out. Never throws for messaging failures.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. A repeat under an idempotency key
    /// that already produced a resend returns that resend without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's text both in this application and at the provider.
    /// The record of the message (and its outcome) survives.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Best-effort refresh of a notification's delivery outcome from the provider.</summary>
    Task RefreshOutcomeAsync(OrderNotification notification, CancellationToken ct = default);
}

public class ResendResult
{
    public OrderNotification? Notification { get; set; }
    public bool AlreadyExisted { get; set; }
    public ResendFailure? Failure { get; set; }
}

public enum ResendFailure
{
    NotFound,
    ContentDisposed,
    NothingToResend
}
