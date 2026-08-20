using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public PlaceOrderAddressRequest? ShipToAddress { get; set; }
    public string? BuyerId { get; set; }
}

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlaceOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public int? ParentNotificationId { get; set; }

    public static OrderNotificationDto From(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind,
            Body = notification.ContentDisposed ? null : notification.Body,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            ContentDisposed = notification.ContentDisposed,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            ParentNotificationId = notification.ParentNotificationId
        };
    }
}

public static class OrderResponseMapper
{
    public static Address? ToAddress(PlaceOrderAddressRequest? request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Street)
            || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.Country)
            || string.IsNullOrWhiteSpace(request.ZipCode))
        {
            return null;
        }

        return new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
    }

    public static List<PlaceOrderItem> ToItems(IEnumerable<PlaceOrderItemRequest> items)
    {
        return items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
    }
}
