using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendOutcome
{
    Sent,
    AlreadyProcessed,
    NotificationNotFound,
    ContentRedacted,
    DestinationNoLongerRegistered
}

public record ResendResult(ResendOutcome Outcome, OrderNotification? Notification, string? Error = null);

public enum ReconciliationMatchState
{
    Matched,
    ProviderOnly,
    LocalOnly
}

public record ReconciliationEntry(
    string? MessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? LocalStatus,
    DateTimeOffset? DateSent,
    ReconciliationMatchState MatchState);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries)
{
    public int ProviderMessageCount { get; init; }
    public int LocalNotificationCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int LocalOnlyCount { get; init; }
}

/// <summary>
/// Orchestrates shopper notifications as orders move. Sending is best-effort:
/// a message that cannot be sent is recorded and never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the order's notifications, refreshing non-terminal outcomes from the provider.
    /// Returns null when the order does not exist or does not belong to the caller (unless the caller is an operator).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string callerId, bool callerIsOperator, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper.
    /// Repeating under the same idempotency key returns the original resend without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text both locally and at the provider.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lines up the provider's record of messages against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
