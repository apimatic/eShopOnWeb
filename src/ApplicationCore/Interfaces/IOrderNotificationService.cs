using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS notification capability: a shopper's contact numbers, the messages that go
/// out as an order moves, and the operator actions taken on those messages. A message that cannot be
/// sent never fails the underlying order operation; a shopper with no number on file is simply not
/// messaged. All shopper-scoped operations act only on the caller's own data.
/// </summary>
public interface IOrderNotificationService
{
    // ---- Flow 1: contact numbers (shopper-scoped) ----
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumber>> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken = default);
    /// <returns>true if a number owned by the caller was removed; false if none matched.</returns>
    Task<bool> RemoveContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);

    // ---- Flow 2: orders and their notifications ----
    Task<OrderWithNotifications> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);
    /// <returns>The notifications raised, or null if no such order exists.</returns>
    Task<IReadOnlyList<OrderNotification>?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithNotifications>> ListOrdersWithNotificationsAsync(string buyerId, CancellationToken cancellationToken = default);
    /// <returns>The order's notifications (delivery outcomes refreshed), or null if the caller owns no such order.</returns>
    Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    // ---- Flow 3: operator actions ----
    /// <returns>The notification for the re-sent message (new, or the existing one under a repeated key), or null if the source notification does not exist.</returns>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    /// <returns>true if the content was disposed; false if no such notification exists.</returns>
    Task<bool> DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: how many of a catalog item to order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order together with the notifications raised for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent,
/// over a date range, so discrepancies in either direction are visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderMessage> OnlyAtProvider,
    IReadOnlyList<ReconciliationLocalRecord> OnlyInEShop);

/// <summary>A message present in both the provider's record and eShop's, matched by provider message SID.</summary>
public record ReconciliationMatch(
    int NotificationId,
    string ProviderMessageSid,
    NotificationKind Kind,
    int OrderId,
    string? LocalStatus,
    string? ProviderStatus,
    bool StatusMatches);

/// <summary>An eShop notification the provider's range did not account for.</summary>
public record ReconciliationLocalRecord(
    int NotificationId,
    string? ProviderMessageSid,
    NotificationKind Kind,
    int OrderId,
    string LocalStatus,
    DateTimeOffset CreatedAt);
