using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class RegisterContactNumberRequest
{
    public string Number { get; init; } = string.Empty;
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; init; } = new();
    public ShippingAddressRequest? ShippingAddress { get; init; }
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;

    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(Street) && !string.IsNullOrWhiteSpace(City)
        && !string.IsNullOrWhiteSpace(Country) && !string.IsNullOrWhiteSpace(ZipCode);

    public Address ToDomain() => new(Street, City, State, Country, ZipCode);
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record ContactNumberDto(int ContactNumberId, string Number, DateTimeOffset CreatedAt)
{
    public static ContactNumberDto From(ContactNumber contact) =>
        new(contact.Id, contact.CanonicalNumber, contact.CreatedAt);
}

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
    DateTimeOffset? LastProviderSyncAt,
    bool ProviderStateStale,
    DateTimeOffset? ContentDisposedAt,
    int? OriginalNotificationId)
{
    public static NotificationDto From(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.Body,
        notification.ProviderMessageSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.ProviderErrorMessage,
        notification.CreatedAt,
        notification.ScheduledFor,
        notification.ProviderDateSent,
        notification.LastProviderSyncAt,
        notification.ProviderStateStale,
        notification.ContentDisposedAt,
        notification.OriginalNotificationId);
}

public sealed record MyOrderItemDto(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);

public sealed record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Progress,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CancelledAt,
    decimal Total,
    IReadOnlyList<MyOrderItemDto> Items,
    IReadOnlyList<NotificationDto> Notifications)
{
    public static MyOrderDto From(Order order, IReadOnlyList<NotificationDto> notifications) => new(
        order.Id,
        order.OrderDate,
        order.Progress.ToString(),
        order.DispatchedAt,
        order.CancelledAt,
        order.Total(),
        order.OrderItems.Select(x => new MyOrderItemDto(
            x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        notifications);
}

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Messages);

public sealed record ReconciliationRow(
    string? ProviderMessageId,
    int? NotificationId,
    int? OrderId,
    bool ExistsAtProvider,
    bool ExistsInEShop,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? ProviderDateSent,
    int? ProviderErrorCode)
{
    public static ReconciliationRow From(ProviderMessageRecord? provider, OrderNotification? local) => new(
        provider?.Sid ?? local?.ProviderMessageSid,
        local?.Id,
        local?.OrderId,
        provider is not null,
        local is not null,
        provider?.Status,
        local?.ProviderStatus,
        provider?.DateSent,
        provider?.ErrorCode);
}
