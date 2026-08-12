using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// ---- inbound requests ----

/// <summary>One catalog line on a placed order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

// ---- outbound views ----

/// <summary>A shopper's registered contact number, as returned to that shopper (its owner).</summary>
public record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

/// <summary>A single notification about an order, with the provider-owned delivery state.</summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Type,
    string Status,
    string? ProviderStatusRaw,
    string? ProviderMessageId,
    int? ErrorCode,
    string? ErrorMessage,
    bool IsFollowUp,
    bool ContentDisposed,
    string? Body,
    string? ToNumberMasked,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>An order line, for the my-orders view.</summary>
public record OrderLineView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order with its lines and the notifications raised about it (where they got to).</summary>
public record OrderView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineView> Items,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>The result of a resend request.</summary>
public record ResendResult(int NotificationId, bool Replayed);

// ---- reconciliation ----

/// <summary>A provider message lined up against what eShop believes it sent.</summary>
public record ReconciliationEntry(
    string ProviderMessageId,
    string Presence,
    string? ProviderStatus,
    int? NotificationId,
    string? EShopStatus,
    int? OrderId,
    int? ErrorCode,
    DateTimeOffset? DateSent);

/// <summary>
/// A reconciliation report over a date range: the provider's own record of messages from this
/// application's sending number, lined up against eShop's notification records so a message one
/// side knows about and the other does not is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
