using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// How far one notification got — carries its own <c>notificationId</c> (what the operator endpoints
/// act on), the provider's identifier, and its current delivery outcome.
/// </summary>
public record NotificationSummaryDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string? ProviderMessageSid,
    string? Status,
    bool ContentDisposed,
    DateTimeOffset? ScheduledFor,
    int? ErrorCode,
    DateTimeOffset CreatedAt);

public static class NotificationMapping
{
    public static NotificationSummaryDto ToSummary(OrderNotification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        n.ProviderMessageSid,
        n.Status,
        n.ContentDisposed,
        n.ScheduledFor,
        n.ErrorCode,
        n.CreatedAt);
}

/// <summary>Route + identity for GET /api/orders/{orderId}/notifications.</summary>
public record OrderNotificationsRequest(int OrderId, string BuyerId);

public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationSummaryDto> Notifications);

/// <summary>Route + idempotency key for POST /api/notifications/{notificationId}/resend.</summary>
public record ResendNotificationRequest(int NotificationId, string? IdempotencyKey);

/// <summary>Response of a resend — the id of the message the resend produced.</summary>
public record ResendNotificationResponse(int NotificationId, string? ProviderMessageSid, string? Status);

/// <summary>Query for GET /api/notifications/reconciliation.</summary>
public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

public record ReconciliationEntryDto(
    string? Sid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    string? EShopStatus,
    string? Kind);

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntryDto> Matched,
    IReadOnlyList<ReconciliationEntryDto> ProviderOnly,
    IReadOnlyList<ReconciliationEntryDto> EShopOnly)
{
    public static ReconciliationEntryDto MapEntry(ReconciliationEntry e) => new(
        e.Sid, e.ProviderStatus, e.ProviderDateSent, e.NotificationId, e.EShopStatus, e.Kind?.ToString());
}
