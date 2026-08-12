using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Generic outcome of an action that targets an order or notification by id.</summary>
public enum ActionOutcome
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    Invalid = 3
}

/// <summary>
/// A shopper- and operator-facing view of one notification. Carries the provider identifier and the
/// current delivery outcome. Never carries the destination number.
/// </summary>
public record NotificationView(
    int NotificationId,
    string Kind,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    bool ContentRedacted,
    DateTimeOffset? ScheduledSendAt,
    DateTimeOffset CreatedAt,
    int? OrderId);

/// <summary>An order together with where each of its notifications got to.</summary>
public record OrderSummary(
    int OrderId,
    string Status,
    decimal Total,
    DateTimeOffset OrderDate,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>One row of the reconciliation report.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? DateSent);

/// <summary>
/// Provider vs eShop reconciliation over a date range, restricted to the configured sending number.
/// <see cref="ProviderOnly"/> is what the provider knows about that eShop does not; <see cref="EShopOnly"/>
/// is what eShop believes it sent that the provider does not report.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
