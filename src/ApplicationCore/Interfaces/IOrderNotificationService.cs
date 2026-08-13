using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Keeps shoppers informed by text message as their orders move, and gives operators the tools to act on
/// messages after the fact (resend, dispose of content, reconcile).
/// <para>
/// None of the "notify" operations may fail the underlying business operation: a message that cannot be sent
/// is recorded as such and the caller's request still succeeds. A shopper with no number on file is simply
/// not messaged.
/// </para>
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a follow-up with the provider for a few days later
    /// asking how the delivery went.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out so a
    /// cancelled order never prompts a "how did your delivery go?" message.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-send a message that did not reach the shopper. The idempotency key makes a repeat of the same request
    /// return the message the first attempt produced instead of sending another; a fresh key sends again.
    /// </summary>
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider and locally, keeping the record that it was sent and what
    /// became of it.
    /// </summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Load an order's notifications, refreshing their delivery outcomes from the provider first.</summary>
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refresh the delivery outcomes of the given notifications from the provider.</summary>
    Task RefreshStatusesAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line up the provider's own record of messages sent from the configured sending number against what
    /// eShop believes it sent, over a date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a resend request.</summary>
/// <param name="ResultNotificationId">The notification the resend produced (or reused).</param>
/// <param name="AlreadyProcessed">True when this idempotency key had already been used and no new message was sent.</param>
public record ResendOutcome(int ResultNotificationId, bool AlreadyProcessed);

/// <summary>How a reconciliation entry lines up between the provider and eShop.</summary>
public enum ReconciliationMatch
{
    /// <summary>Both the provider and eShop have the message.</summary>
    Matched = 0,

    /// <summary>The provider knows about the message but eShop does not.</summary>
    ProviderOnly = 1,

    /// <summary>eShop believes it sent the message but the provider did not return it.</summary>
    EShopOnly = 2
}

/// <summary>One reconciled message.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    ReconciliationMatch Match,
    string? ProviderStatus,
    int? ProviderErrorCode,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus,
    string? MaskedTo,
    DateTimeOffset? DateSent);

/// <summary>A reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopMessageCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
