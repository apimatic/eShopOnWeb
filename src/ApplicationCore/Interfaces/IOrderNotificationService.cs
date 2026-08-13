using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notifications that accompany an order's lifecycle and the operator actions
/// on the messages that result. Every notify method is best-effort: a message that cannot be sent
/// is recorded as such but never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper the order is on its way and queue a delivery follow-up for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper the order is cancelled and call off any follow-up that has not yet gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Re-send a message that did not reach the shopper, honouring the caller-supplied idempotency key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's content — locally and at the provider — while its record survives.</summary>
    Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Line up the provider's own record of messages against what eShop believes it sent, over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Refresh non-terminal notifications' delivery outcomes from the provider and persist any change.</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>The notifications recorded for an order, optionally refreshed against the provider first.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool refresh, CancellationToken cancellationToken = default);

    /// <summary>All notifications recorded for a shopper, optionally refreshed against the provider first.</summary>
    Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, bool refresh, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a resend request.</summary>
public record ResendResult(ResendOutcome Outcome, OrderNotification? Notification, string? Error)
{
    public static ResendResult Sent(OrderNotification n) => new(ResendOutcome.Sent, n, null);
    public static ResendResult IdempotentReplay(OrderNotification n) => new(ResendOutcome.IdempotentReplay, n, null);
    public static ResendResult NotFound() => new(ResendOutcome.NotFound, null, null);
    public static ResendResult Rejected(string error) => new(ResendOutcome.Rejected, null, error);
}

public enum ResendOutcome
{
    Sent,
    IdempotentReplay,
    NotFound,
    Rejected
}

/// <summary>Outcome of a content-disposal request.</summary>
public record ContentDisposalResult(bool Found, bool ProviderRedacted, string? Error)
{
    public static ContentDisposalResult NotFound() => new(false, false, null);
    public static ContentDisposalResult Disposed(bool providerRedacted) => new(true, providerRedacted, null);
    public static ContentDisposalResult Failed(string error) => new(true, false, error);
}

/// <summary>A reconciliation of provider records against eShop's own, over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report. Phone numbers are deliberately omitted.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? ProviderDateSent);
