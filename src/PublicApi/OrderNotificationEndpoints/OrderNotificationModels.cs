using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed record PlaceOrderResponse(int OrderId);

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record NotificationResponse(
    int NotificationId,
    string Type,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? Content,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderSentAt,
    int? ResendOfNotificationId);

public sealed record OrderResponse(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CancelledAt,
    decimal Total,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<NotificationResponse> Notifications);

public sealed record MyOrdersResponse(IReadOnlyList<OrderResponse> Orders);

public sealed record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationResponse> Notifications);

public sealed record ReconciliationEntryResponse(
    string Match,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? ApplicationStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt,
    string? MaskedDestination);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntryResponse> Entries);
