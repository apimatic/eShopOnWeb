using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Place an order from catalog items (the caller's identity comes from the token).</summary>
public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Response for a placed order. Returns the new id as top-level <c>orderId</c>.</summary>
public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>An order in the caller's my-orders listing, showing where its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>Response for an operator dispatch/cancel action, echoing the resulting notifications.</summary>
public class OrderOperationResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>What was sent for an order and what became of it. Carries its own <c>notificationId</c>.</summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }

    /// <summary>The provider's message identifier. Not PII; the operator endpoints act on it via NotificationId.</summary>
    public string? ProviderMessageSid { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public bool IsResend { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static OrderNotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderMessageSid = n.ProviderMessageSid,
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        ContentRedacted = n.ContentRedacted,
        IsResend = n.IsResend,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
