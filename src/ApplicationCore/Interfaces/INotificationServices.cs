using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Result of an operator resend.</summary>
/// <param name="NotificationId">The notification the resend produced (or the one an earlier request under the same key produced).</param>
/// <param name="AlreadyProcessed">True when this key had already been used — no second message was sent.</param>
public record ResendOutcome(int NotificationId, bool AlreadyProcessed);

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationEntry(
    string Disposition,
    string? ProviderSid,
    string? ProviderStatus,
    int? NotificationId,
    string? LocalStatus);

/// <summary>Reconciliation of the provider's record against eShop's, over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int LocalCount,
    IReadOnlyList<ReconciliationEntry> Entries,
    bool Truncated,
    int PagesRead);

/// <summary>Dispositions used in <see cref="ReconciliationEntry.Disposition"/>.</summary>
public static class ReconciliationDisposition
{
    public const string Matched = "matched";
    public const string MissingLocally = "provider-only";   // provider knows, eShop doesn't
    public const string MissingAtProvider = "eshop-only";   // eShop believes it sent, provider doesn't
}

/// <summary>Manages a shopper's registered contact numbers, scoped to the owner.</summary>
public interface IContactNumberService
{
    /// <summary>Validate and register a number for the owner; returns the new id. Throws when the number is unusable.</summary>
    Task<int> RegisterAsync(string ownerId, string rawNumber, CancellationToken ct);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct);

    /// <summary>Remove one of the owner's numbers. Returns false when it does not exist or is not the owner's.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken ct);
}

/// <summary>Places orders and drives the SMS notifications as an order moves.</summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the buyer from catalog items; notify "placed". Returns the order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct);

    /// <summary>Operator: mark dispatched, notify "on its way", and queue a delivery follow-up a few days out.</summary>
    Task DispatchOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel the order, notify, and call off any pending follow-up.</summary>
    Task CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: re-send a notification that did not reach the shopper, idempotent on the key.</summary>
    Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>Shopper/operator: dispose of a message's content at the provider and locally.</summary>
    Task RedactNotificationContentAsync(int notificationId, CancellationToken ct);

    /// <summary>Operator: reconcile the provider's record against eShop's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
