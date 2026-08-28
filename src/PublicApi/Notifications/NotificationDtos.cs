using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed record RegisterContactNumberRequest(string Number);
public sealed record RegisterContactNumberResponse(int ContactNumberId);
public sealed record ContactNumberResponse(int ContactNumberId, string Number, DateTimeOffset RegisteredAt);

public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record PlaceOrderResponse(int OrderId);
public sealed record OrderActionResponse(int OrderId, string Status);

public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);

public sealed record NotificationResponse(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    bool ContentDisposed,
    string LocalOutcome,
    string? ProviderMessageId,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? LastProviderSyncAt,
    bool ProviderStateStale,
    int? OriginalNotificationId);

public sealed record MyOrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<NotificationResponse> Notifications);

public sealed record ReconciliationEntry(
    string Match,
    int? NotificationId,
    string? ProviderMessageId,
    string? ProviderStatus,
    string? ProviderDateSent);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string BoundarySemantics,
    int ProviderCount,
    int LocalCount,
    IReadOnlyList<ReconciliationEntry> Entries);

public sealed class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message) { }
}
