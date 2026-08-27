using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode = null);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items, ShippingAddressRequest? ShippingAddress = null);
public sealed record PlaceOrderResponse(int OrderId, IReadOnlyList<int> NotificationIds);

public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string? ProviderMessageId,
    string ProviderStatus,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ContentDeletedAt,
    int? OriginalNotificationId);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record OrderTransitionResponse(
    int OrderId,
    string Status,
    IReadOnlyList<int> NotificationIds);

public sealed record ReconciliationEntry(
    string ProviderMessageId,
    int? NotificationId,
    string Presence,
    string? LocalStatus,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
