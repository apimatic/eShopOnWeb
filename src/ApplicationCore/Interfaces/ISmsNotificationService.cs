using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates the shopper-facing and operator-facing SMS notification flows on top of the order model
/// and the <see cref="ISmsGateway"/>. Every method is separately invocable; sending is best-effort and
/// never fails the underlying order operation.
/// </summary>
public interface ISmsNotificationService
{
    // ---- Flow 1: the shopper's contact number ----
    Task<RegisterNumberResult> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct = default);
    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct = default);
    /// <summary>Removes a number the caller owns. Returns false if it is not the caller's or does not exist.</summary>
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct = default);

    // ---- Flow 2: messages as the order moves ----
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default);
    Task<OrderTransitionResult> DispatchOrderAsync(int orderId, CancellationToken ct = default);
    Task<OrderTransitionResult> CancelOrderAsync(int orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
    Task<Order?> GetOrderAsync(int orderId, CancellationToken ct = default);
    /// <summary>Returns the notifications for an order, refreshing any non-terminal outcomes from the provider first.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default);

    // ---- Flow 3: what the operator can do about it ----
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);
    Task<DisposeResultCode> DisposeContentAsync(int notificationId, CancellationToken ct = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>A requested order line: a catalog item and how many units.</summary>
public record OrderLineRequest(int CatalogItemId, int Units);

/// <summary>Result of registering a number: either the stored number, or a rejection reason for a bad one.</summary>
public record RegisterNumberResult(ContactNumber? Number, string? RejectionReason)
{
    public bool Rejected => Number is null;
}

public enum OrderTransitionResult
{
    Success,
    AlreadyInState,
    OrderNotFound
}

public enum ResendResultCode
{
    Resent,
    ReplayedIdempotent,
    NotificationNotFound,
    ContentDisposed,
    NumberRemoved
}

public record ResendOutcome(ResendResultCode Code, int? NotificationId, string? DeliveryStatus);

public enum DisposeResultCode
{
    Disposed,
    NotFound
}

/// <summary>One reconciled message, carrying whichever side(s) know about it.</summary>
public record ReconciliationEntry(
    string Sid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus,
    NotificationKind? Kind);

/// <summary>
/// The provider's own record of messages for a range, lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
