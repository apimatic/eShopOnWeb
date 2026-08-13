using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
}

public class CreateOrderResponse
{
    /// <summary>Identifier of the created order (top-level, so the flow can be driven onward).</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = "placed";
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}

public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// A notification's public view: the identifier operator endpoints act on, plus where the
/// message got to. The destination number and message text are deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public int? ErrorCode { get; set; }
    public bool IsScheduled { get; set; }
    public bool ContentDeleted { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderSid = n.ProviderSid,
        ErrorCode = n.ErrorCode,
        IsScheduled = n.IsScheduled,
        ContentDeleted = n.ContentDeleted,
        CreatedDate = n.CreatedDate
    };
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
