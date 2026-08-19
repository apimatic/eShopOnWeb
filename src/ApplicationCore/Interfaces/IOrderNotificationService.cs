using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A message that
/// cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the shopper from catalog item ids + quantities. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken);

    /// <summary>Operator action: mark an order dispatched and queue the delivery follow-up. Returns false if no such order.</summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: cancel an order and call off any follow-up not yet sent. Returns false if no such order.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>The caller's own orders, each with where its notifications got to.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Notifications for an order the caller owns. Returns null when the order is not the caller's / not found.</summary>
    Task<IReadOnlyList<OrderNotificationView>?> GetOrderNotificationsAsync(string ownerId, int orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Operator action: resend a message that did not reach the shopper. Idempotent on
    /// <paramref name="idempotencyKey"/>: a repeat under the same key returns the existing resend
    /// without sending again. Returns the notification id of the resend, or null if the source
    /// notification does not exist.
    /// </summary>
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Operator action: dispose of a message's content at the provider and here. Returns false if no such notification.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>Operator action: reconcile the provider's record of messages against eShop's over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>A requested order line.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>A notification as shown to a caller.</summary>
public record OrderNotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string Status,
    string? ProviderMessageSid,
    int? ErrorCode,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    bool ContentDisposed,
    DateTimeOffset CreatedAt);

/// <summary>One of the caller's orders with its notifications.</summary>
public record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<MyOrderItemView> Items,
    IReadOnlyList<OrderNotificationView> Notifications);

public record MyOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>The reconciliation report over a date range for the configured sending number.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> OnlyAtProvider,
    IReadOnlyList<ReconciliationEntry> OnlyInEShop);

public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? Kind,
    string? ProviderStatus,
    string? EShopStatus,
    string? MaskedTo,
    DateTimeOffset? DateSent);
