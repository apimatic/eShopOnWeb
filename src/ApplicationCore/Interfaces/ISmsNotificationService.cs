using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates shopper contact numbers and the SMS that go out as an order moves, plus the
/// operator tooling (resend, content disposal, reconciliation). A message that cannot be sent
/// never fails the underlying operation — the order is still placed/dispatched/cancelled.
/// </summary>
public interface ISmsNotificationService
{
    // --- Flow 1: contact numbers (shopper-scoped) ---

    /// <summary>Validates + canonicalizes then stores a number. Throws PhoneNumberNotUsableException if unusable.</summary>
    Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not the caller's / not found.</summary>
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct = default);

    // --- Flow 2: order-lifecycle messages ---

    /// <summary>
    /// Places an order from catalog item ids + quantities (reusing the existing Order/OrderItem model),
    /// then tells the shopper it was placed. The caller's identity is the buyer.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Tells the shopper their order was placed. Never throws out to the caller.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>Operator: mark dispatched, tell the shopper, queue a follow-up. Returns false if the order is unknown.</summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancel, tell the shopper, call off the queued follow-up. Returns false if the order is unknown.</summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>The caller's own orders (with items).</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>The buyer that owns an order, or null if the order does not exist (for ownership checks).</summary>
    Task<string?> GetOrderBuyerAsync(int orderId, CancellationToken ct = default);

    /// <summary>The notifications for an order, optionally refreshed from the provider first.</summary>
    Task<IReadOnlyList<SmsNotification>> GetNotificationsForOrderAsync(int orderId, bool refresh, CancellationToken ct = default);

    /// <summary>A single notification by id.</summary>
    Task<SmsNotification?> GetNotificationAsync(int notificationId, CancellationToken ct = default);

    // --- Flow 3: operator tooling ---

    /// <summary>Operator: re-send a message that did not reach the shopper, gated by an idempotency key.</summary>
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Operator: dispose of a message's content at the provider (redaction). Returns false if unknown.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Operator: reconcile the provider's record of FromNumber traffic against eShop's over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public enum ResendStatus { NotFound, NotEligible, Created, Replayed }

/// <summary>Outcome of a resend. On Created/Replayed, <see cref="NotificationId"/> is the resulting message.</summary>
public record ResendOutcome(ResendStatus Status, int? NotificationId);

/// <summary>One message lined up across the provider's record and eShop's.</summary>
public record ReconciliationEntry(
    string? Sid,
    bool KnownToProvider,
    bool KnownToEshop,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? EshopNotificationId,
    string? EshopOutcome);

/// <summary>A reconciliation report over a date range for the app's own sending number.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int InBothCount,
    int ProviderOnlyCount,
    int EshopOnlyCount,
    bool Truncated,
    IReadOnlyList<ReconciliationEntry> Entries);
