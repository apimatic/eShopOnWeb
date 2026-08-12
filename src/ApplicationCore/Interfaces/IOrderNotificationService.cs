using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders from catalog items (reusing the existing order/order-item model) and drives the SMS
/// notifications that go out as an order moves. Sends never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper and tells them it was placed. Returns the created order.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken ct = default);

    /// <summary>Operator action: marks the order dispatched, tells the shopper, and queues a delivery follow-up with the provider.</summary>
    Task DispatchAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels the order, tells the shopper, and calls off any queued follow-up before it goes out.</summary>
    Task CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>The shopper's orders, each with its notifications (delivery outcomes refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// The notifications for one order the shopper owns, with delivery outcomes refreshed from the provider.
    /// Throws <see cref="Exceptions.NotFoundException"/> if the order is not the shopper's.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: re-sends a message that did not reach the shopper. The idempotency key makes a
    /// repeat of the same request a no-op (returning the message the first request produced) while a fresh
    /// key sends again. Returns the notification the resend produced.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Operator action on a shopper's behalf: disposes of a message's content at the provider so its text
    /// is no longer retrievable, while the fact it was sent and its outcome survive.
    /// </summary>
    Task RedactNotificationContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: lines the provider's own record of messages from the configured sending number over
    /// a date range up against what this application believes it sent, surfacing either-way discrepancies.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>One line of a new order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications sent about it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>The provider-vs-eShop reconciliation over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationEShopEntry> InEShopOnly,
    IReadOnlyList<ReconciliationProviderEntry> InProviderOnly);

/// <summary>A message present both at the provider and in eShop, matched by provider message id.</summary>
public record ReconciliationMatch(int NotificationId, int OrderId, string Sid, string EShopStatus, string ProviderStatus, int? ProviderErrorCode);

/// <summary>A message eShop believes it sent that the provider's range did not return.</summary>
public record ReconciliationEShopEntry(int NotificationId, int OrderId, string Sid, string EShopStatus);

/// <summary>A message the provider reports from the configured number that eShop has no record of.</summary>
public record ReconciliationProviderEntry(string Sid, string ProviderStatus, int? ProviderErrorCode, DateTimeOffset? DateSent);
