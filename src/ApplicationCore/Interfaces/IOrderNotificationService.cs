using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the existing order model) and drives the SMS notifications that go out
/// as an order moves. A message that cannot be sent never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog item ids + quantities, and tells them it was placed.</summary>
    Task<Result<Order>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a "how did delivery go?"
    /// follow-up with the provider for a few days later. Operator action.
    /// </summary>
    Task<Result> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the order, calls off any not-yet-sent follow-up so it can never reach the shopper, and
    /// tells the shopper it was cancelled. Operator action.
    /// </summary>
    Task<Result> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own orders, each with where its notifications got to.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What was sent for one of the shopper's own orders, and what became of each message.
    /// NotFound if the order is not the caller's (or does not exist).
    /// </summary>
    Task<Result<IReadOnlyList<OrderNotification>>> GetNotificationsForOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. The idempotency key makes a repeat under the
    /// same key a no-op (returning the earlier result), while a fresh key is a genuine new attempt.
    /// Operator action. Returns the notification the resend produced.
    /// </summary>
    Task<Result<OrderNotification>> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content — redacted at the provider and cleared here — while the fact
    /// that it was sent and what became of it survive. Operator action.
    /// </summary>
    Task<Result> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines the provider's own record of messages from the configured sending number, over a date
    /// range, up against what eShop believes it sent. Operator action.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order together with the notifications recorded for it.</summary>
public sealed record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>The reconciliation result over a range for the configured sending number.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FromNumber,
    int ProviderMessageCount,
    int EShopMessageCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One row of a reconciliation report. Destination is masked; the raw number is never surfaced.</summary>
public sealed record ReconciliationEntry(
    string? MessageSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    int? NotificationId,
    int? OrderId,
    string? Kind,
    string? MaskedTo,
    DateTimeOffset? DateSent);
