using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Body of POST /api/orders.</summary>
public class PlaceOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
}

/// <summary>Response of POST /api/orders — carries the new id as a top-level field.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>One message about an order and what became of it. Carries its own notificationId.</summary>
public class NotificationView
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? MessageSid { get; set; }
    public bool IsScheduled { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }

    public static NotificationView From(NotificationSummary n) => new()
    {
        NotificationId = n.NotificationId,
        Kind = n.Kind,
        Status = n.Status,
        MessageSid = n.MessageSid,
        IsScheduled = n.IsScheduled,
        ContentRedacted = n.ContentRedacted,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        CreatedAtUtc = n.CreatedAtUtc,
        SentAtUtc = n.SentAtUtc
    };
}

public class OrderView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();

    public static OrderView From(OrderSummary o) => new()
    {
        OrderId = o.OrderId,
        Status = o.Status,
        OrderDate = o.OrderDate,
        Total = o.Total,
        Notifications = o.Notifications.Select(NotificationView.From).ToList()
    };
}

public class MyOrdersResponse
{
    public List<OrderView> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}
