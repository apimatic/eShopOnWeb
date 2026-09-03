using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and keeps the shopper informed by SMS as an order moves. A message that cannot be sent
/// never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the shopper from catalog items, then tell them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct = default);

    /// <summary>
    /// Operator action: mark an order dispatched, tell the shopper it is on its way, and queue a follow-up
    /// "how did the delivery go" message with the provider for a few days later.
    /// </summary>
    Task DispatchAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: cancel an order, tell the shopper, and call off any follow-up that has not yet gone out.
    /// </summary>
    Task CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>The caller's orders, each with its notifications (provider status refreshed).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>What was sent for one of the caller's orders, with the provider status refreshed.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. The idempotency key makes a
    /// repeat under the same key a no-op (the existing message is returned); a fresh key sends again.
    /// Returns the notification that the resend produced (existing or new).
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Operator action: dispose of a message's content at the provider and locally.</summary>
    Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Operator action: reconcile the provider's own record of sent messages against ours for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>A requested order line: a catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications sent for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>
/// The result of reconciling the provider's record against this application's for a date range: messages
/// both agree on, messages only the provider knows about, and messages only this application knows about.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopMessageCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderMessage> ProviderOnly,
    IReadOnlyList<OrderNotification> EShopOnly);

/// <summary>A message the provider and this application both hold, lined up by provider message id.</summary>
public record ReconciliationMatch(
    int NotificationId,
    int OrderId,
    string Sid,
    string? ProviderStatus,
    string? EShopProviderStatus);
