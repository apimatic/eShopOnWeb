using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

// ---- Requests ----------------------------------------------------------

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted.</summary>
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key: repeats under the same key never send a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- DTOs --------------------------------------------------------------

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    public static OrderDto From(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };
}

/// <summary>
/// A notification as returned to callers. The destination number is deliberately omitted — a shopper's
/// number is never echoed back (and operator views must not expose one shopper's number).
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ContentRedacted = n.ContentRedacted,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        SentAt = n.SentAt,
        CreatedDate = n.CreatedDate,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}

// ---- Responses ---------------------------------------------------------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class OrderStatusResponse
{
    public OrderDto Order { get; set; } = new();
}

public class OrderWithNotificationsDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderWithNotificationsDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public NotificationDto Notification { get; set; } = new();
}

// ---- Reconciliation ----------------------------------------------------

public class ReconciliationEntryDto
{
    public string Sid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopStatus { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        Sid = e.Sid,
        ProviderStatus = e.ProviderStatus,
        DateSent = e.DateSent,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        EShopStatus = e.EShopStatus
    };
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> InProviderNotInEShop { get; set; } = new();
    public List<ReconciliationEntryDto> InEShopNotInProvider { get; set; } = new();

    public static ReconciliationResponse FromReport(ReconciliationReport r) => new()
    {
        From = r.From,
        To = r.To,
        ProviderMessageCount = r.ProviderMessageCount,
        EShopMessageCount = r.EShopMessageCount,
        MatchedCount = r.MatchedCount,
        Matched = r.Matched.Select(ReconciliationEntryDto.From).ToList(),
        InProviderNotInEShop = r.InProviderNotInEShop.Select(ReconciliationEntryDto.From).ToList(),
        InEShopNotInProvider = r.InEShopNotInProvider.Select(ReconciliationEntryDto.From).ToList()
    };
}
