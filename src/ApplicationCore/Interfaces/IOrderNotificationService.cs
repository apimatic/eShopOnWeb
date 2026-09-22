using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the messages that go out as an order moves, and the operator actions on them. Sending is
/// best-effort: a message that cannot be sent is recorded as failed and never propagates out to fail the
/// underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed (one message per registered number).</summary>
    Task SendOrderPlacedAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Tell the shopper the order is on its way, and queue a "how did the delivery go?" follow-up with the
    /// provider for a few days later.
    /// </summary>
    Task SendDispatchedAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Tell the shopper the order was cancelled, and call off any follow-up that has not yet gone out so it
    /// never reaches them.
    /// </summary>
    Task SendCancelledAsync(Order order, CancellationToken ct);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// repeating a request under the same key returns the same result without sending again.
    /// </summary>
    Task<ResendOutcome> ResendAsync(OrderNotification original, string idempotencyKey, CancellationToken ct);

    /// <summary>Dispose of a message's content at the provider and locally; the send record survives.</summary>
    Task RedactContentAsync(OrderNotification notification, CancellationToken ct);

    /// <summary>Refresh non-terminal notifications from the provider so read views show current outcomes.</summary>
    Task RefreshOutcomesAsync(IReadOnlyList<OrderNotification> notifications, CancellationToken ct);

    /// <summary>Line up the provider's record of this sender's messages against eShop's over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Result of a resend: the produced (or already-produced) message's notification id.</summary>
public sealed record ResendOutcome(int NotificationId, bool WasDuplicate);

/// <summary>A reconciliation report over a date range.</summary>
public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Messages present on both sides, keyed by provider Sid.</summary>
    public List<ReconciliationMatch> Matched { get; init; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationProviderOnly> ProviderOnly { get; init; } = new();

    /// <summary>Messages eShop believes it sent (from this sender, in range) that the provider did not return.</summary>
    public List<ReconciliationEShopOnly> EShopOnly { get; init; } = new();

    /// <summary>True if the provider result set was capped before the whole range was walked.</summary>
    public bool ProviderResultsTruncated { get; init; }

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }
}

public sealed record ReconciliationMatch(string Sid, int NotificationId, int OrderId, NotificationKind Kind,
    string? ProviderStatus, string LocalDeliveryState, string? DateSent);

public sealed record ReconciliationProviderOnly(string Sid, string? Status, string? To, string? DateSent);

public sealed record ReconciliationEShopOnly(int NotificationId, int OrderId, string? Sid, NotificationKind Kind,
    string LocalDeliveryState);
