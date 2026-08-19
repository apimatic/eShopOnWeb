using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>One line of an order request: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order through the API.</summary>
public record ShippingAddress(string Street, string City, string State, string Country, string ZipCode);

/// <summary>A registered contact number as returned to its owner.</summary>
public record ContactNumberView(int ContactNumberId, string E164Number, DateTimeOffset CreatedDate);

/// <summary>
/// A notification about an order and what became of it. Carries its own identifier (what the
/// operator endpoints act on) and the provider-owned delivery state. The destination number is
/// deliberately not exposed here.
/// </summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    bool ContentDisposed,
    bool ContentAvailable,
    string? ProviderMessageSid,
    int? ResendOfNotificationId,
    DateTimeOffset CreatedDate);

/// <summary>An order with where each of its notifications got to.</summary>
public record OrderSummaryView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>
/// One line of the reconciliation report: a message the provider knows about, one eShop believes
/// it sent, or both lined up together.
/// </summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    int? OrderId,
    string? Kind,
    string? EShopStatus);

/// <summary>
/// The reconciliation report over a date range: what the provider's own record holds for the
/// configured sender, lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string Sender,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
