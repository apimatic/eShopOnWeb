using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested catalog line for placing an order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationEntry(
    string Sid,
    string? Status,
    string? To,
    string Source);   // "matched" | "provider_only" | "eshop_only"

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>Outcome of a resend request (idempotent by caller-supplied key).</summary>
public record ResendResult(bool Found, int NotificationId, bool WasDuplicate);

/// <summary>
/// Orchestrates order placement and the notifications that go out as an order moves. A failure to
/// send a message never fails the underlying order operation.
/// </summary>
public interface IOrderMessagingService
{
    /// <summary>Place an order for the shopper from catalog lines (reusing the existing Order model),
    /// then tell them it was placed. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct);

    /// <summary>Mark an order dispatched: tell the shopper it is on its way and queue the delivery
    /// follow-up with the provider for a few days later. Returns false if the order does not exist.</summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Cancel an order: call off any not-yet-sent follow-up and tell the shopper.
    /// Returns false if the order does not exist.</summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Re-send a message that did not reach the shopper. Idempotent on
    /// <paramref name="idempotencyKey"/>. Returns the id of the notification the resend produced
    /// (or the existing one for a repeated key).</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Dispose of the provider's copy of a message's content. Returns false if not found.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Reconcile the provider's record against eShop's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Notifications for one order, with delivery outcomes refreshed from the provider
    /// (best effort). Returns null if the order does not exist.</summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, CancellationToken ct);

    /// <summary>Refresh the delivery outcomes of all a shopper's notifications from the provider
    /// (best effort), so a subsequent read shows where each got to.</summary>
    Task RefreshBuyerNotificationsAsync(string buyerId, CancellationToken ct);
}
