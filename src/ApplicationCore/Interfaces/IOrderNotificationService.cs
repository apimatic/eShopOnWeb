using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the existing Order/OrderItem model) and drives the SMS notifications that go
/// out as an order moves. A message that cannot be sent never fails the underlying operation.
/// Shopper-facing reads are scoped to the caller; operator actions act across orders.
/// </summary>
public interface IOrderNotificationService
{
    // ----- Shopper -----
    Task<int> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotificationView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Notifications for one of the caller's own orders. Null when the caller has no such order.</summary>
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string ownerId, int orderId, CancellationToken cancellationToken = default);

    // ----- Operator -----
    /// <summary>Marks the order dispatched, tells the shopper, and queues the delivery follow-up with the provider. Null when the order does not exist.</summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order, tells the shopper, and calls off any follow-up not yet sent. False when the order does not exist.</summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's content at the provider and locally. False when the notification does not exist.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item id and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order plus where each of its notifications got to.</summary>
public record OrderNotificationView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineView> Items,
    IReadOnlyList<NotificationView> Notifications);

public record OrderLineView(int CatalogItemId, string ProductName, int Units, decimal UnitPrice);

/// <summary>One notification as reported to a caller. Never carries the destination number or message body.</summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Type,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    bool ContentRedacted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? ScheduledFor);

public record ResendResult(ResendOutcome Outcome, int? NotificationId, string? Error)
{
    public static ResendResult Sent(int notificationId) => new(ResendOutcome.Sent, notificationId, null);
    public static ResendResult Duplicate(int notificationId) => new(ResendOutcome.DuplicateIgnored, notificationId, null);
    public static ResendResult NotFound() => new(ResendOutcome.NotFound, null, null);
    public static ResendResult Failed(string error) => new(ResendOutcome.Failed, null, error);
}

public enum ResendOutcome
{
    Sent,
    DuplicateIgnored,
    NotFound,
    Failed
}

/// <summary>
/// The provider's record of messages for a range lined up against what eShop believes it sent, so a
/// message one side knows about and the other does not is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of the reconciliation report. Carries no destination number.</summary>
public record ReconciliationEntry(
    string ProviderMessageSid,
    string? Status,
    int? NotificationId,
    DateTimeOffset? DateSent);
