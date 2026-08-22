using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public bool ContentRedacted { get; set; }

    public static NotificationDto From(NotificationView view) => new()
    {
        NotificationId = view.NotificationId,
        Kind = view.Kind.ToString(),
        ProviderMessageSid = view.ProviderMessageSid,
        Status = view.Status,
        Body = view.Body,
        ErrorCode = view.ErrorCode,
        CreatedAt = view.CreatedAt,
        ScheduledFor = view.ScheduledFor,
        DateSent = view.DateSent,
        ContentRedacted = view.ContentRedacted
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
