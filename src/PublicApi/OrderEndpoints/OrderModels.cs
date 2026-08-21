using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
}

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
}

public class ShopperOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<ShopperOrderResponse> Orders { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public static class NotificationDtoMapper
{
    public static NotificationDto FromEntity(Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate.OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            ProviderSid = notification.ProviderSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            Error = notification.ProviderError,
            Body = notification.ContentRedacted ? null : notification.Body,
            DateSent = notification.ProviderDateSent,
            ScheduledFor = notification.ScheduledFor,
            ContentRedacted = notification.ContentRedacted
        };
    }
}
