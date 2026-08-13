using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS a shopper receives as their order moves, and the operator actions over those
/// messages. Every "notify" here is best-effort: a message that cannot be sent is recorded but never
/// fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls off any not-yet-sent follow-up for the order (so it can never reach the shopper) and tells
    /// the shopper the order was cancelled.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>The notifications for an order, with their delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The notifications across several orders (for summarising a shopper's orders), delivery state refreshed.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(int[] orderIds, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the provider-owned delivery state of the given notifications in place.</summary>
    Task RefreshStatusesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>Operator re-send of a message that did not reach the shopper, deduplicated by idempotency key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content at the shopper's request — redacted at the provider and cleared locally.</summary>
    Task<ContentDisposalOutcome> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's own record of sent messages against what eShop believes it sent, over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public enum ResendOutcome
{
    /// <summary>A new message was sent and recorded.</summary>
    Sent,

    /// <summary>The same idempotency key was seen before; nothing new was sent.</summary>
    DuplicateIgnored,

    /// <summary>No notification with the given id exists.</summary>
    OriginalNotFound,

    /// <summary>The number the original was addressed to has been removed, so nothing may be sent to it again.</summary>
    DestinationRemoved,

    /// <summary>The original message's content was disposed of and cannot be re-sent.</summary>
    ContentDisposed
}

public class ResendResult
{
    public ResendOutcome Outcome { get; init; }

    /// <summary>The id of the message the resend produced, or of the existing message on a duplicate.</summary>
    public int? NotificationId { get; init; }
}

public enum ContentDisposalOutcome
{
    Disposed,
    NotFound
}

/// <summary>One line of a reconciliation report. Never carries a shopper's number.</summary>
public class ReconciliationEntry
{
    public string? Sid { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public DateTimeOffset? SentAt { get; init; }
}

/// <summary>
/// The provider's record of messages from the application's sending number over a range, lined up
/// against eShop's own records so that either side knowing about a message the other does not is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>The application's own configured sending number the report covers.</summary>
    public string SendingNumber { get; init; } = string.Empty;

    public int ProviderMessageCount { get; init; }
    public int EShopMessageCount { get; init; }
    public int MatchedCount { get; init; }

    /// <summary>Messages both sides agree on, by SID.</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider lists that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent that the provider does not list.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}
