using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SendAt { get; set; }
    public int? ParentNotificationId { get; set; }
    public string? LocalFailure { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        DeliveryStatus = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        Body = notification.Body,
        ContentDisposed = notification.ContentDisposed,
        CreatedAt = notification.CreatedAt,
        SendAt = notification.SendAt,
        ParentNotificationId = notification.ParentNotificationId,
        LocalFailure = notification.LocalFailure
    };
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
