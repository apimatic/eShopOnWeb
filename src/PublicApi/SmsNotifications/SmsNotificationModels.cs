using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

// Response DTOs for the SMS-notification surface. The shopper's raw destination number is
// deliberately not echoed in notification responses; operators act on the notificationId.

public class ContactNumberDto
{
    public int ContactNumberId { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public DateTimeOffset CreatedDate { get; init; }
}

public class NotificationDto
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string Kind { get; init; } = string.Empty;

    /// <summary>Current delivery outcome as owned by the provider (or a local marker before/without a send).</summary>
    public string Status { get; init; } = string.Empty;

    public string? ProviderMessageSid { get; init; }
    public string? Body { get; init; }
    public bool ContentDisposed { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedDate { get; init; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

public class OrderSummaryDto
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public List<OrderItemDto> Items { get; init; } = new();

    /// <summary>Where each of this order's notifications got to.</summary>
    public List<NotificationDto> Notifications { get; init; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; init; }
    public int? NotificationId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

public static class SmsNotificationMapping
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
        Kind = n.Kind.ToString(),
        Status = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        Body = n.Body,
        ContentDisposed = n.ContentDisposed,
        ScheduledFor = n.ScheduledFor,
        DateSent = n.ProviderDateSent,
        ErrorCode = n.ProviderErrorCode,
        ErrorMessage = n.ProviderErrorMessage,
        CreatedDate = n.CreatedDate
    };

    public static OrderItemDto ToDto(this OrderItem i) => new()
    {
        CatalogItemId = i.ItemOrdered.CatalogItemId,
        ProductName = i.ItemOrdered.ProductName,
        UnitPrice = i.UnitPrice,
        Units = i.Units
    };

    public static OrderSummaryDto ToSummaryDto(this Order order, IEnumerable<Notification> notifications) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Items = order.OrderItems.Select(i => i.ToDto()).ToList(),
        Notifications = notifications.Select(n => n.ToDto()).ToList()
    };
}
