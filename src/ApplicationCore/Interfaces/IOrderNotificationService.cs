using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates the order lifecycle with the SMS notifications that go out as an order moves. A message
/// that cannot be sent never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    // --- Contact numbers (shopper-scoped) ---
    Task<ContactNumber> RegisterNumberAsync(string ownerId, string rawNumber, CancellationToken ct);
    Task<IReadOnlyList<ContactNumber>> GetNumbersAsync(string ownerId, CancellationToken ct);
    Task<bool> DeleteNumberAsync(string ownerId, int contactNumberId, CancellationToken ct);

    // --- Orders (POST is shopper-scoped; dispatch/cancel are operator) ---
    Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct);
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken ct);
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct);

    // --- Reads ---
    Task<Order?> GetOrderForOwnerAsync(int orderId, string ownerId, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetOrdersForOwnerAsync(string ownerId, CancellationToken ct);
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken ct);
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOwnerAsync(string ownerId, bool refreshFromProvider, CancellationToken ct);

    // --- Operator actions on notifications ---
    Task<ResendOutcome?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>One line of a place-order request: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>The result of a resend: the notification the resend produced, and whether the key had already been used.</summary>
public record ResendOutcome(OrderNotification Notification, bool WasReplay);

/// <summary>The provider's record of messages for a range, lined up against what eShop believes it sent.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderMessageRecord> ProviderOnly,
    IReadOnlyList<ReconciliationEshopOnly> EshopOnly);

/// <summary>A message present both in the provider's record and in eShop's.</summary>
public record ReconciliationMatch(string ProviderSid, int NotificationId, int OrderId, string? ProviderStatus, string? EshopStatus);

/// <summary>A message eShop believes it sent but the provider has no record of in the range.</summary>
public record ReconciliationEshopOnly(int NotificationId, int OrderId, string ProviderSid, string? EshopStatus);
