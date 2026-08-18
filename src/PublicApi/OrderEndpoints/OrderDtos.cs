using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>
/// One notification for an order and what became of it. <see cref="NotificationId"/> is the identifier the
/// operator endpoints act on.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The current delivery outcome (mostly the provider's own status).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, when it reached the provider.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };
}

/// <summary>An order with its items and where each of its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}
