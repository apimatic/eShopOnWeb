using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that accompany an order as it moves. Each action
/// is separately invocable. A messaging failure never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order from catalog items for the shopper, and tell them it was placed.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput? shipTo, CancellationToken ct);

    /// <summary>
    /// Operator: mark the order dispatched, tell the shopper, and queue the "how did delivery go"
    /// follow-up with the provider for a few days later. Returns false if the order does not exist.
    /// </summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Operator: cancel the order, tell the shopper, and call off any not-yet-sent follow-up so it
    /// never reaches them. Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>The caller's orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<OrderSummaryView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// What was sent for this order and what became of each message. Returns null if the order does
    /// not exist or does not belong to <paramref name="buyerId"/>. Refreshes live outcomes from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken ct);

    /// <summary>
    /// Operator: re-send a message that did not reach the shopper, deduplicated by
    /// <paramref name="idempotencyKey"/>. Returns null if the notification does not exist / cannot be re-sent.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Operator: dispose of a message's content at the provider and locally. Returns false if the
    /// notification does not exist or was never sent.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Operator: reconcile the provider's record against what eShop believes it sent, over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

public sealed record OrderSummaryView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderNotificationView> Notifications);

public sealed record OrderNotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string ChannelStatus,
    string? MessageSid,
    int? ErrorCode,
    string? ErrorDescription,
    bool IsScheduled,
    DateTimeOffset? ScheduledSendAt,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ResendResult(int NotificationId, bool Deduplicated);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    bool ProviderResultTruncated,
    IReadOnlyList<ReconciliationEntry> InBoth,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

public sealed record ReconciliationEntry(
    string? MessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? ProviderDateSent);
