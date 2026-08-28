using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed record ContactNumberCreatedResponse(int ContactNumberId);
public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderLineRequest
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

public sealed record OrderCreatedResponse(int OrderId);
public sealed record OrderStateChangedResponse(int OrderId, string Status);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<OrderLineResponse> Items,
    IReadOnlyList<NotificationResponse> Notifications);

public sealed record OrderLineResponse(int CatalogItemId, string ProductName, int Quantity, decimal UnitPrice);

public sealed record NotificationResponse(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    bool ContentDisposed,
    string? ProviderMessageId,
    string DeliveryStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ScheduledFor,
    int? OriginalNotificationId);

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record NotificationCreatedResponse(int NotificationId);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int ApplicationCount,
    IReadOnlyList<ReconciliationEntryResponse> Entries);

public sealed record ReconciliationEntryResponse(
    string? ProviderMessageId,
    int? NotificationId,
    int? OrderId,
    string Presence,
    string? ProviderStatus,
    string? ApplicationStatus,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt);
