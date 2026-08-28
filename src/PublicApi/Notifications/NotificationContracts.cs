using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class RegisterContactNumberRequest
{
    [Required, MaxLength(64)]
    public string MobileNumber { get; init; } = string.Empty;
}

public sealed record ContactNumberCreatedResponse(Guid ContactNumberId);
public sealed record ContactNumberResponse(Guid ContactNumberId, string MobileNumber, DateTimeOffset CreatedAt);

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public IReadOnlyList<PlaceOrderItemRequest> Items { get; init; } = [];
    public ShippingAddressRequest? ShippingAddress { get; init; }
}

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderCreatedResponse(int OrderId);
public sealed record OrderStateResponse(int OrderId, string Status);

public sealed class ResendNotificationRequest
{
    [Required, MinLength(1), MaxLength(128)]
    public string IdempotencyKey { get; init; } = string.Empty;
}
public sealed record NotificationCreatedResponse(Guid NotificationId);

public sealed record NotificationResponse(
    Guid NotificationId,
    string Kind,
    string SubmissionStatus,
    string? ProviderSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    string? ProviderError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? SentAt,
    string CancellationState,
    string RedactionState,
    string? Content,
    bool ContentAvailable,
    bool ProviderRefreshSucceeded);

public sealed record MyOrderResponse(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CancelledAt,
    decimal Total,
    IReadOnlyList<NotificationResponse> Notifications);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntryResponse> Entries);

public sealed record ReconciliationEntryResponse(
    string Match,
    Guid? NotificationId,
    string? ProviderSid,
    string? ApplicationStatus,
    string? ProviderStatus,
    DateTimeOffset? ApplicationCreatedAt,
    DateTimeOffset? ProviderDate);
