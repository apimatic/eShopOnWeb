using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Coarse outcome of a service operation, mapped to an HTTP status by the endpoint.</summary>
public enum ActionOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2,
    BadRequest = 3,
    Forbidden = 4
}

// ---- Contact numbers ----

public record RegisterContactNumberResult(ActionOutcome Outcome, int ContactNumberId, string? PhoneNumber, string? Error);

public record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedDate);

// ---- Orders & notifications ----

public record PlaceOrderResult(ActionOutcome Outcome, int OrderId, string? Error);

public record OrderActionResult(ActionOutcome Outcome, int OrderId, string Status, string? Error);

public record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string Status,
    int? ErrorCode,
    bool ContentRedacted,
    bool Scheduled,
    DateTimeOffset? ScheduledSendAt,
    int? ResendOfNotificationId,
    string? ProviderMessageSid,
    DateTimeOffset CreatedDate);

public record OrderLineView(int CatalogItemId, string ProductName, int Units, decimal UnitPrice);

public record OrderView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineView> Items,
    IReadOnlyList<NotificationView> Notifications);

public record OrderNotificationsResult(ActionOutcome Outcome, IReadOnlyList<NotificationView> Notifications, string? Error);

public record ResendResult(ActionOutcome Outcome, int NotificationId, string Status, string? Error);

public record DisposeContentResult(ActionOutcome Outcome, string? Error);

// ---- Reconciliation ----

public record ReconciliationMatch(string ProviderMessageSid, int NotificationId, int OrderId, string Kind,
    string ProviderStatus, string EShopStatus);

public record ReconciliationProviderOnly(string ProviderMessageSid, string ProviderStatus, string? To, DateTimeOffset? DateSent);

public record ReconciliationEShopOnly(int NotificationId, string? ProviderMessageSid, int OrderId, string Kind, string EShopStatus);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderMessageCount,
    int EShopMessageCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderOnly> OnlyInProvider,
    IReadOnlyList<ReconciliationEShopOnly> OnlyInEShop);
