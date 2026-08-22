using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static OrderNotificationDto From(OrderNotificationView view) => new()
    {
        NotificationId = view.NotificationId,
        OrderId = view.OrderId,
        Kind = view.Kind.ToString(),
        Body = view.Body,
        ContentDisposed = view.ContentDisposed,
        ProviderSid = view.ProviderSid,
        ProviderStatus = view.ProviderStatus,
        ErrorCode = view.ErrorCode,
        ErrorMessage = view.ErrorMessage,
        CreatedUtc = view.CreatedUtc,
        ScheduledForUtc = view.ScheduledForUtc,
        ResendOfNotificationId = view.ResendOfNotificationId
    };
}

public class ShopperOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMyOrdersResponse()
    {
    }

    public List<ShopperOrderDto> Orders { get; set; } = new();
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListOrderNotificationsResponse()
    {
    }

    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
