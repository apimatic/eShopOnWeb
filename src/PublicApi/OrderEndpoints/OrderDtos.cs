using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderShippingAddress(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Body for placing an order. Identity comes from the token, not this body.</summary>
public record PlaceOrderRequest(List<PlaceOrderItem> Items, PlaceOrderShippingAddress? ShippingAddress);

// Commands carry the caller identity (extracted from the JWT in the route delegate).
public record PlaceOrderCommand(string BuyerId, List<PlaceOrderItem> Items, PlaceOrderShippingAddress? ShippingAddress);
public record DispatchOrderCommand(int OrderId);
public record CancelOrderCommand(int OrderId);
public record MyOrdersCommand(string OwnerId);
public record OrderNotificationsQuery(int OrderId, string CallerId, bool IsAdministrator);

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>A single notification and what became of it. <see cref="NotificationId"/> is what the
/// operator endpoints act on.</summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Type,
    string? ProviderMessageSid,
    string? Status,
    int? ProviderErrorCode,
    bool IsScheduled,
    DateTimeOffset? ScheduledSendAt,
    bool ContentRedacted,
    DateTimeOffset CreatedDate)
{
    public static NotificationDto From(OrderNotification n) => new(
        n.Id, n.OrderId, n.Type.ToString(), n.ProviderMessageSid, n.ProviderStatus,
        n.ProviderErrorCode, n.IsScheduled, n.ScheduledSendAt, n.ContentRedacted, n.CreatedDate);
}

public record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    List<OrderLineDto> Items,
    List<NotificationDto> Notifications)
{
    public static MyOrderDto From(Order order, IEnumerable<OrderNotification> notifications) => new(
        order.Id,
        order.OrderDate,
        order.Total(),
        order.OrderItems.Select(i => new OrderLineDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        notifications.Select(NotificationDto.From).ToList());
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderOperationResponse
{
    public int OrderId { get; set; }
    public string Message { get; set; } = string.Empty;
}
