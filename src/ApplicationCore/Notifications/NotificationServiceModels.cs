using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>A line of an order-placement request: a catalog item and how many of it.</summary>
public record OrderRequestItem(int CatalogItemId, int Quantity);

/// <summary>Outcome of an operator resend. On a repeated idempotency key, <see cref="Deduplicated"/> is true
/// and <see cref="NotificationId"/> is the original resend's notification — no second message was sent.</summary>
public record ResendResult(int NotificationId, bool Deduplicated, string? ProviderMessageSid, DeliveryOutcome Outcome);

/// <summary>One notification's state, as shown to a caller.</summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string? ProviderMessageSid,
    string? ProviderStatus,
    DeliveryOutcome Outcome,
    int? ErrorCode,
    bool SendFailed,
    bool ContentDisposed,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset CreatedAt);

/// <summary>An order plus where each of its notifications got to.</summary>
public record OrderSummary(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>One message lined up between the provider's record and eShop's.</summary>
public record ReconciliationEntry(
    string Sid,
    string Presence, // "Matched" | "ProviderOnly" | "EShopOnly"
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    string? Kind);

/// <summary>The reconciliation report over a window, filtered to the configured sending number.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    bool Truncated,
    int PagesFetched,
    IReadOnlyList<ReconciliationEntry> Entries);
