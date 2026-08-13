using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders (reusing the app's existing Order/OrderItem model) and drives the SMS
/// notifications that go out as an order moves. A message that cannot be sent never fails the
/// underlying operation — the order is still placed, dispatched or cancelled, and the record of
/// the failed send survives so an operator can resend it.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order from catalog item ids + quantities for the shopper, then notify them. Returns the new order id.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct = default);

    /// <summary>Operator action: mark the order dispatched, notify the shopper, and queue the delivery follow-up with the provider.</summary>
    Task<OrderActionResult> DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel the order, notify the shopper, and call off any not-yet-sent follow-up.</summary>
    Task<OrderActionResult> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>The shopper's own orders, each with its notifications (delivery outcomes refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// The notifications for one order, scoped to the owning shopper (returns null if the order is not the
    /// caller's / does not exist). Delivery outcomes are refreshed from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForOwnerAsync(int orderId, string buyerId, CancellationToken ct = default);

    /// <summary>Operator action: re-send a message that did not reach the shopper, idempotent under the caller-supplied key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Operator action: dispose of a message's content here and at the provider; the send record survives.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Operator action: reconcile the provider's own record of FromNumber traffic in a range against eShop's records.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>A requested order line: how many of a catalog item.</summary>
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with its notifications, for the shopper-facing my-orders view.</summary>
public sealed record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public enum PlaceOrderStatus { Placed, InvalidRequest }

public sealed record PlaceOrderResult(PlaceOrderStatus Status, int OrderId, string? Error)
{
    public static PlaceOrderResult Placed(int orderId) => new(PlaceOrderStatus.Placed, orderId, null);
    public static PlaceOrderResult Invalid(string error) => new(PlaceOrderStatus.InvalidRequest, 0, error);
}

public enum OrderActionStatus { Done, OrderNotFound }

public sealed record OrderActionResult(OrderActionStatus Status, IReadOnlyList<OrderNotification> Notifications)
{
    public static readonly OrderActionResult NotFound = new(OrderActionStatus.OrderNotFound, System.Array.Empty<OrderNotification>());
    public static OrderActionResult Completed(IReadOnlyList<OrderNotification> notifications) => new(OrderActionStatus.Done, notifications);
}

public enum ResendStatus { Resent, NotificationNotFound, NothingResendable }

public sealed record ResendResult(ResendStatus Status, OrderNotification? Notification, string? Reason)
{
    public static ResendResult Resent(OrderNotification n) => new(ResendStatus.Resent, n, null);
    public static readonly ResendResult NotFound = new(ResendStatus.NotificationNotFound, null, null);
    public static ResendResult CannotResend(string reason) => new(ResendStatus.NothingResendable, null, reason);
}

/// <summary>The reconciliation of provider traffic against eShop's records for a date range.</summary>
public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderOnly> KnownToProviderOnly,
    IReadOnlyList<ReconciliationEShopOnly> KnownToEShopOnly);

public sealed record ReconciliationMatch(string ProviderMessageSid, string? ProviderStatus, int NotificationId, NotificationKind Kind, string EShopStatus);

public sealed record ReconciliationProviderOnly(string ProviderMessageSid, string? ProviderStatus, string? To, DateTimeOffset? DateSent);

public sealed record ReconciliationEShopOnly(int NotificationId, string ProviderMessageSid, NotificationKind Kind, string EShopStatus);
