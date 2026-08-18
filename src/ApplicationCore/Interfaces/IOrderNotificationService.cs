using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that go out as an order moves. A message that
/// cannot be sent never fails the underlying operation: the order is still placed, dispatched, or
/// cancelled and the caller's request still succeeds.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for a shopper from catalog items, reusing the app's own Order/OrderItem model,
    /// then tell the shopper it was placed.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct);

    /// <summary>
    /// Mark an order dispatched, tell the shopper it is on its way, and queue a delivery follow-up with
    /// the provider for a few days later.
    /// </summary>
    Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Cancel an order, call off any delivery follow-up that has not yet gone out, and tell the shopper.
    /// </summary>
    Task<OrderActionResult> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>The caller's own orders, each showing where its notifications got to.</summary>
    Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// What was sent for an order and what became of each message. Returns null when the order does not
    /// exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(int orderId, string callerId, CancellationToken ct);

    /// <summary>
    /// Re-send a message that did not reach the shopper. Repeating a request under the same idempotency
    /// key returns the message the first attempt produced without sending a second one.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider and locally. Returns false if no such notification.
    /// </summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken ct);

    /// <summary>
    /// Line up the provider's own record of messages for a date range (asked of the provider, filtered
    /// to the application's configured sending number) against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public enum OrderActionResult
{
    Success = 0,
    NotFound = 1,

    /// <summary>The order is not in a state from which this action is allowed.</summary>
    InvalidState = 2
}

public enum ResendOutcome
{
    Sent = 0,

    /// <summary>An earlier request under the same idempotency key already produced a message; that one is returned.</summary>
    Duplicate = 1,
    NotFound = 2
}

public record ResendResult(ResendOutcome Outcome, Notification? Notification);

/// <summary>An order plus the notifications it raised, for the shopper's own view.</summary>
public record MyOrderView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>
/// A notification and its current outcome. Deliberately omits the destination number and message body.
/// </summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string DeliveryStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    bool ContentRedacted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderMessageCount,
    int EShopMessageCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationDiscrepancy> ProviderOnly,
    IReadOnlyList<ReconciliationDiscrepancy> EShopOnly,
    IReadOnlyList<ReconciliationMatch> Matched);

/// <summary>A message one side knows about and the other does not.</summary>
public record ReconciliationDiscrepancy(string? Sid, string? ProviderStatus, int? NotificationId, string Detail);

/// <summary>A message both sides agree on, with each side's status.</summary>
public record ReconciliationMatch(string Sid, string? ProviderStatus, int NotificationId, string LocalStatus);
