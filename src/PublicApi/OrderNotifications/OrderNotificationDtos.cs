using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed record RegisterContactNumberRequest(string PhoneNumber);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string CanonicalNumber);
public sealed record ContactNumberDto(int ContactNumberId, string CanonicalNumber, DateTimeOffset CreatedAt);
public sealed record ContactNumberListResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderResponse(int OrderId);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string ProviderStatus,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    bool ContentDisposed,
    bool RefreshFailed,
    bool CancellationPending,
    int? OriginalNotificationId);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record MyOrdersResponse(IReadOnlyList<OrderDto> Orders);
public sealed record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);
public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);
public sealed record ContentDisposalResponse(int NotificationId, bool ContentDisposed);

public sealed record ReconciliationEntryDto(
    string Match,
    int? NotificationId,
    string? ProviderMessageSid,
    string? ApplicationStatus,
    string? ProviderStatus,
    string? ProviderDateCreated,
    string? ProviderDateSent,
    int? ProviderErrorCode);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntryDto> Messages);

public sealed class OrderNotificationApiException : Exception
{
    public OrderNotificationApiException(int statusCode, string safeMessage) : base(safeMessage)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
