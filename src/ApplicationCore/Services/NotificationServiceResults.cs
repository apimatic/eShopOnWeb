using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Whether a caller may act on the addressed resource.</summary>
public enum AccessOutcome { Ok, NotFound, Forbidden }

/// <summary>Outcome of registering a contact number.</summary>
public record ContactNumberRegistrationResult(bool Accepted, string? RejectionReason, ContactNumber? ContactNumber);

/// <summary>Outcome of placing an order.</summary>
public record PlaceOrderResult(bool Success, int? OrderId, string? Error);

/// <summary>An order paired with the notifications sent for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);

public enum ResendStatus
{
    /// <summary>A new message was sent (or attempted) under a fresh key.</summary>
    Sent,
    /// <summary>The key had already been used — the earlier message is returned, nothing re-sent.</summary>
    ReplayedExisting,
    SourceNotFound,
    /// <summary>The source message's content has been disposed of, so there is nothing to re-send.</summary>
    ContentUnavailable
}

public record ResendResult(ResendStatus Status, int? NotificationId);

public enum DisposeStatus { Ok, NotFound }

public record DisposeResult(DisposeStatus Status);

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent,
/// over a date range, counting only messages sent from the application's configured sending number.
/// </summary>
public record ReconciliationReport(
    System.DateTimeOffset From,
    System.DateTimeOffset To,
    string SendingNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>One line of the reconciliation: a message the provider and/or eShop knows about.</summary>
public record ReconciliationEntry(
    string? ProviderMessageId,
    string Presence,          // "matched" | "provider-only" | "eshop-only"
    string? ProviderStatus,
    int? NotificationId,
    string? EShopStatus,
    int? OrderId);
