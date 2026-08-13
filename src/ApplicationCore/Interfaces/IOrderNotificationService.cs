using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order lifecycle and the SMS notifications that go out as an order moves. A
/// message that cannot be sent never fails the underlying operation: the order is still placed,
/// dispatched or cancelled and the caller's request still succeeds. A shopper with no number on
/// file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog items for the shopper, then tells them it was placed.</summary>
    Task<OrderPlacementResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a "how did the
    /// delivery go?" follow-up with the provider for a few days later.
    /// </summary>
    Task<OrderOperationResult> DispatchOrderAsync(int orderId);

    /// <summary>
    /// Cancels the order, calls off any not-yet-sent delivery follow-up so it can never reach the
    /// shopper, and tells the shopper the order was cancelled.
    /// </summary>
    Task<OrderOperationResult> CancelOrderAsync(int orderId);

    /// <summary>
    /// Loads an order's notifications (with the owning buyer for scoping), refreshing each one's
    /// delivery outcome from the provider. Returns null if the order does not exist.
    /// </summary>
    Task<OrderNotificationsView?> GetOrderNotificationsAsync(int orderId);

    /// <summary>
    /// The caller's own orders, each with its notifications and their current delivery outcome.
    /// </summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// repeating under the same key returns the message already produced without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey);

    /// <summary>
    /// Disposes a message's content at the shopper's request: the text is redacted at the provider
    /// (no longer retrievable there) and cleared here, while the record of the message survives.
    /// </summary>
    Task<ContentDisposalResult> DisposeContentAsync(int notificationId);

    /// <summary>
    /// Builds a reconciliation report over a date range: the provider's own record of messages from
    /// the configured sending number, lined up against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>A requested catalog item and quantity for a new order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public class OrderPlacementResult
{
    public bool Succeeded { get; init; }
    public Order? Order { get; init; }
    public string? Error { get; init; }

    public static OrderPlacementResult Success(Order order) => new() { Succeeded = true, Order = order };
    public static OrderPlacementResult Invalid(string error) => new() { Succeeded = false, Error = error };
}

public enum OrderOperationOutcome
{
    Success,
    NotFound,
    InvalidState
}

public class OrderOperationResult
{
    public OrderOperationOutcome Outcome { get; init; }
    public Order? Order { get; init; }
    public string? Error { get; init; }

    public static OrderOperationResult Success(Order order) => new() { Outcome = OrderOperationOutcome.Success, Order = order };
    public static OrderOperationResult NotFound() => new() { Outcome = OrderOperationOutcome.NotFound };
    public static OrderOperationResult InvalidState(string error) => new() { Outcome = OrderOperationOutcome.InvalidState, Error = error };
}

public class ResendResult
{
    public bool Found { get; init; }
    public bool Reused { get; init; }
    public Notification? Notification { get; init; }
    public string? Error { get; init; }

    public static ResendResult NotFound() => new() { Found = false };
    public static ResendResult Sent(Notification notification) => new() { Found = true, Reused = false, Notification = notification };
    public static ResendResult AlreadyHandled(Notification notification) => new() { Found = true, Reused = true, Notification = notification };
    public static ResendResult Failed(string error) => new() { Found = true, Error = error };
}

public class ContentDisposalResult
{
    public bool Found { get; init; }
    public Notification? Notification { get; init; }

    public static ContentDisposalResult NotFound() => new() { Found = false };
    public static ContentDisposalResult Disposed(Notification notification) => new() { Found = true, Notification = notification };
}

/// <summary>Reconciliation of provider records against eShop's beliefs over a date range.</summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;

    public int ProviderCount { get; init; }
    public int EShopCount { get; init; }

    public List<ReconciliationEntry> Matched { get; init; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; init; } = new();

    /// <summary>Messages eShop believes it sent that the provider did not return for this number/range.</summary>
    public List<ReconciliationEntry> EShopOnly { get; init; } = new();
}

/// <summary>An order's notifications together with the buyer that owns the order, for scoping reads.</summary>
public class OrderNotificationsView
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public IReadOnlyList<Notification> Notifications { get; init; } = new List<Notification>();
}

/// <summary>An order paired with its notifications.</summary>
public class OrderWithNotifications
{
    public Order Order { get; init; } = default!;
    public IReadOnlyList<Notification> Notifications { get; init; } = new List<Notification>();
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public int? NotificationId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
