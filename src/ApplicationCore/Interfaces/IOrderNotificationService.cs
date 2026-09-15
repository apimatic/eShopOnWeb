using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives the SMS notifications that follow an order as it moves. A message that cannot
/// be sent never fails the underlying operation — the order is still placed, dispatched or cancelled.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog item ids + quantities, then notifies "order placed".</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddress? shippingAddress, CancellationToken cancellationToken);

    /// <summary>Marks the order dispatched, notifies the shopper, and queues a delivery-feedback follow-up with the provider.</summary>
    Task<OrderActionResult> DispatchAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Cancels the order, notifies the shopper, and calls off any follow-up that has not yet gone out.</summary>
    Task<OrderActionResult> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Returns the caller's orders, each with the current state of its notifications.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the notifications for one of the caller's orders, refreshing non-terminal delivery outcomes
    /// from the provider. Returns null when the order does not exist or is not the caller's.
    /// </summary>
    Task<IReadOnlyList<Notification>?> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Idempotent on <paramref name="idempotencyKey"/>:
    /// repeating under the same key returns the earlier result without sending again. Returns null when the
    /// source notification does not exist.
    /// </summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. Returns false when the notification does
    /// not exist. Throws a provider exception if the provider redaction fails.
    /// </summary>
    Task<bool?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>
    /// Builds a reconciliation report over [<paramref name="from"/>, <paramref name="to"/>], comparing the
    /// provider's own record of messages sent from the configured sending number against what eShop believes
    /// it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>A requested order line: a catalog item id and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order. Defaulted when not supplied.</summary>
public record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);

public record PlaceOrderResult(bool Success, int OrderId, string? Error);

public enum OrderActionStatus
{
    Success,
    NotFound,
    InvalidState
}

public record OrderActionResult(OrderActionStatus Status, string? Message);

public record ResendResult(int NotificationId, bool Deduplicated);

/// <summary>An order paired with the notifications that have gone out for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);

/// <summary>A message lined up between the provider's record and eShop's record.</summary>
public record ReconciliationEntry(string? Sid, string? ProviderStatus, string? EShopStatus, int? NotificationId, int? OrderId);

/// <summary>
/// The reconciliation result: messages present in both, only at the provider, or only in eShop.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
