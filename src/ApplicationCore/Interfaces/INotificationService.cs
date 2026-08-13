using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order-notification capability: contact numbers, order lifecycle messaging,
/// and the operator actions on the messages that go out. Sending never fails the underlying
/// operation — a message that cannot be sent is recorded and the operation still succeeds.
/// </summary>
public interface INotificationService
{
    // ---- Flow 1: contact numbers (shopper-scoped) -----------------------------------------
    Task<ContactNumber> RegisterContactNumberAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string ownerId, CancellationToken cancellationToken = default);
    Task DeleteContactNumberAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);

    // ---- Flow 2: orders and their messages ------------------------------------------------
    Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);   // operator
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);      // operator

    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetOrderNotificationsAsync(string callerId, int orderId, CancellationToken cancellationToken = default); // shopper-scoped

    // ---- Flow 3: operator actions ---------------------------------------------------------
    Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order together with the notifications sent about it and where each of them got to.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent,
/// over a date-time range, for the application's configured sending number.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Messages both the provider and eShop know about (matched by provider message id).</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages the provider knows about but eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent from the configured number but the provider did not return.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = Array.Empty<ReconciliationEntry>();
}

/// <summary>One line of a reconciliation report.</summary>
public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public DateTimeOffset? ProviderDateSent { get; init; }

    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? EShopStatus { get; init; }
}
