using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode = null);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record ContactNumberListResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State,
    string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items,
    ShippingAddressRequest? ShippingAddress = null);
public sealed record PlaceOrderResponse(int OrderId);
public sealed record OrderStateResponse(int OrderId, string Status);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    bool ContentDisposed,
    string ProviderStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? LastCheckedAt,
    int? OriginalNotificationId,
    DateTimeOffset? CancellationRequestedAt,
    int? CancellationErrorCode);

public sealed record NotificationListResponse(IReadOnlyList<NotificationDto> Notifications);

public sealed record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);
public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);

public sealed record ReconciliationEntry(
    string Source,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string ProviderStatus,
    DateTimeOffset? ProviderDateSent);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> ApplicationOnly);
