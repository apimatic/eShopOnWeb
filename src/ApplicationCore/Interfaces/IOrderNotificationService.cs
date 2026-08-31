using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends SMS notifications as orders move. Notification failures never fail the
/// underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Re-sends a message that did not reach the shopper. Repeating the request
    /// under the same idempotency key returns the original resend without sending again.</summary>
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text both at the provider and locally. The fact
    /// that a message was sent, and its outcome, survive.</summary>
    Task<RedactContentResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the provider-owned delivery state of a notification that is not yet terminal.</summary>
    Task RefreshFromProviderAsync(OrderNotification notification, CancellationToken cancellationToken = default);
}

public enum ResendOutcome
{
    Resent = 0,
    IdempotentReplay = 1,
    NotFound = 2,
    DestinationRemoved = 3,
    ContentRedacted = 4
}

public class ResendNotificationResult
{
    public ResendOutcome Outcome { get; }
    public OrderNotification? Notification { get; }

    private ResendNotificationResult(ResendOutcome outcome, OrderNotification? notification)
    {
        Outcome = outcome;
        Notification = notification;
    }

    public static ResendNotificationResult Resent(OrderNotification notification) => new(ResendOutcome.Resent, notification);
    public static ResendNotificationResult IdempotentReplay(OrderNotification notification) => new(ResendOutcome.IdempotentReplay, notification);
    public static ResendNotificationResult NotFound() => new(ResendOutcome.NotFound, null);
    public static ResendNotificationResult DestinationRemoved() => new(ResendOutcome.DestinationRemoved, null);
    public static ResendNotificationResult ContentRedacted() => new(ResendOutcome.ContentRedacted, null);
}

public enum RedactOutcome
{
    Redacted = 0,
    NotFound = 1,
    ProviderError = 2
}

public class RedactContentResult
{
    public RedactOutcome Outcome { get; }
    public string? Error { get; }

    private RedactContentResult(RedactOutcome outcome, string? error)
    {
        Outcome = outcome;
        Error = error;
    }

    public static RedactContentResult Redacted() => new(RedactOutcome.Redacted, null);
    public static RedactContentResult NotFound() => new(RedactOutcome.NotFound, null);
    public static RedactContentResult ProviderError(string error) => new(RedactOutcome.ProviderError, error);
}
