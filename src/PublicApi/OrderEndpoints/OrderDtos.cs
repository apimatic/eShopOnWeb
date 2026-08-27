using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        Status = n.Status,
        MessageSid = n.MessageSid,
        Body = n.Body,
        ContentRedacted = n.ContentRedacted,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
