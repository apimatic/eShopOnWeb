using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; init; } = string.Empty;
}

public sealed record RegisterContactNumberResponse(int ContactNumberId);

public sealed record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed record ContactNumberListResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

public sealed class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; init; } = new();
    public ShippingAddressRequest? ShippingAddress { get; init; }
}

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string? State { get; init; }
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId);

public sealed record OrderTransitionResponse(int OrderId, string Status);

public sealed record OrderItemDto(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record ShippingAddressDto(
    string Street,
    string City,
    string State,
    string Country,
    string ZipCode);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CancelledAt,
    decimal Total,
    ShippingAddressDto ShippingAddress,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record MyOrdersResponse(IReadOnlyList<OrderDto> Orders);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    int ContactNumberId,
    string Kind,
    string Status,
    string? Content,
    bool IsContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderSentAt,
    DateTimeOffset? ProviderUpdatedAt,
    DateTimeOffset? CancellationRequestedAt,
    DateTimeOffset? CancellationCompletedAt,
    int? SourceNotificationId);

public sealed record OrderNotificationsResponse(
    int OrderId,
    IReadOnlyList<NotificationDto> Notifications);

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);

public sealed record ReconciliationDto(
    string Match,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? LocalStatus,
    DateTimeOffset? Timestamp);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationDto> Items);

internal static class OrderNotificationDtoMapper
{
    internal static NotificationDto ToDto(OrderNotification notification) =>
        new(
            notification.Id,
            notification.OrderId,
            notification.ContactNumberId,
            notification.Kind.ToString(),
            notification.Status.ToString(),
            notification.Body,
            notification.IsContentDisposed,
            notification.CreatedAt,
            notification.ScheduledFor,
            notification.ProviderMessageSid,
            notification.ProviderStatus,
            notification.ProviderErrorCode,
            notification.ProviderCreatedAt,
            notification.ProviderSentAt,
            notification.ProviderUpdatedAt,
            notification.CancellationRequestedAt,
            notification.CancellationCompletedAt,
            notification.SourceNotificationId);

    internal static OrderDto ToDto(Order order, IReadOnlyList<OrderNotification> notifications) =>
        new(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.DispatchedAt,
            order.CancelledAt,
            order.Total(),
            new ShippingAddressDto(
                order.ShipToAddress.Street,
                order.ShipToAddress.City,
                order.ShipToAddress.State,
                order.ShipToAddress.Country,
                order.ShipToAddress.ZipCode),
            order.OrderItems.Select(item => new OrderItemDto(
                item.ItemOrdered.CatalogItemId,
                item.ItemOrdered.ProductName,
                item.UnitPrice,
                item.Units)).ToList(),
            notifications.Select(ToDto).ToList());

    internal static ReconciliationDto ToDto(ReconciliationItem item) =>
        new(
            item.Match switch
            {
                ReconciliationMatch.Matched => "matched",
                ReconciliationMatch.ProviderOnly => "providerOnly",
                ReconciliationMatch.LocalOnly => "localOnly",
                _ => throw new ArgumentOutOfRangeException(nameof(item), item.Match, "Unknown reconciliation match.")
            },
            item.ProviderMessageSid,
            item.NotificationId,
            item.OrderId,
            item.ProviderStatus,
            item.LocalStatus,
            item.Timestamp);
}
