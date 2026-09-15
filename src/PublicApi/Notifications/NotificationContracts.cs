using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed record RegisterContactNumberRequest(string PhoneNumber);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);
public sealed record ContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record CreateOrderResponse(int OrderId);
public sealed record OrderActionResponse(int OrderId, string Status);
public sealed record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record OrderDto(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    IReadOnlyList<OrderItemDto> Items, IReadOnlyList<NotificationSummaryDto> Notifications);
public sealed record MyOrdersResponse(IReadOnlyList<OrderDto> Orders);

public sealed record NotificationSummaryDto(int NotificationId, string Kind, string Status,
    int? ProviderErrorCode, DateTimeOffset? ScheduledFor, DateTimeOffset? ProviderDateSent, bool ContentDisposed);
public sealed record NotificationDto(int NotificationId, int OrderId, string Kind, string Status,
    string? ProviderMessageSid, int? ProviderErrorCode, string? Content,
    DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? ProviderDateSent,
    bool ContentDisposed, int? OriginalNotificationId);
public sealed record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);

public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);

public sealed record ReconciliationEntry(string ProviderMessageSid, int? NotificationId,
    bool ExistsInEshop, bool ExistsInProvider, string? EshopStatus, string? ProviderStatus,
    DateTimeOffset? ProviderDateCreated, DateTimeOffset? ProviderDateSent);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    int MatchedCount, int ProviderOnlyCount, int EshopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Messages);

public sealed class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}
