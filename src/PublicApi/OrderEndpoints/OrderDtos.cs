using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? RelatedNotificationId { get; set; }

    public static OrderNotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = ToCamel(notification.Kind),
        ProviderStatus = notification.ProviderStatus,
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        Body = notification.Body,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt,
        ScheduledAt = notification.ScheduledAt,
        ProviderDateSent = notification.ProviderDateSent,
        RelatedNotificationId = notification.RelatedNotificationId
    };

    private static string ToCamel(OrderNotificationKind kind)
    {
        var name = kind.ToString();
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();

    public static OrderSummaryDto From(Order order, IEnumerable<OrderNotification> notifications)
    {
        var dto = new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = ToCamel(order.Status),
            OrderDate = order.OrderDate,
            Total = order.Total()
        };

        foreach (var item in order.OrderItems)
        {
            dto.Items.Add(new OrderLineDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }

        foreach (var notification in notifications)
        {
            dto.Notifications.Add(OrderNotificationDto.From(notification));
        }

        return dto;
    }

    private static string ToCamel(OrderStatus status)
    {
        var name = status.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipTo { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = "123 Main Street";
    public string City { get; set; } = "Seattle";
    public string State { get; set; } = "WA";
    public string Country { get; set; } = "USA";
    public string ZipCode { get; set; } = "98101";
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ListMyOrdersRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
