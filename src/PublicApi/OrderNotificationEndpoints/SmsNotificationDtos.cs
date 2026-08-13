using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

/// <summary>
/// A notification as seen by the shopper who owns the order. Carries its own <see cref="NotificationId"/>
/// (what the operator endpoints act on) and the provider-owned delivery state.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The message recipient (the caller's own registered number).</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }

    /// <summary>The provider's identifier for the message.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, undelivered, failed, scheduled, canceled...).</summary>
    public string? Status { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();

    /// <summary>The notifications sent about this order and where each of them got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Maps the notification domain entities to their API representations.</summary>
public static class SmsNotificationMapper
{
    public static ContactNumberDto ToDto(this ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.PhoneNumber,
        CreatedDate = c.CreatedDate
    };

    public static NotificationDto ToDto(this Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Recipient = n.Recipient,
        Body = n.Body,
        ContentDisposed = n.ContentDisposed,
        ProviderMessageSid = n.ProviderMessageSid,
        Status = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        CreatedDate = n.CreatedDate
    };

    public static OrderItemDto ToDto(this OrderItem i) => new()
    {
        CatalogItemId = i.ItemOrdered.CatalogItemId,
        ProductName = i.ItemOrdered.ProductName,
        UnitPrice = i.UnitPrice,
        Units = i.Units
    };

    public static OrderSummaryDto ToSummaryDto(this OrderWithNotifications o) => new()
    {
        OrderId = o.Order.Id,
        Status = o.Order.Status.ToString(),
        OrderDate = o.Order.OrderDate,
        Total = o.Order.Total(),
        Items = o.Order.OrderItems.Select(i => i.ToDto()).ToList(),
        Notifications = o.Notifications.Select(n => n.ToDto()).ToList()
    };
}
