using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.PublicApi.NotificationsFeature;

/// <summary>A single notification as reported back to a caller. Carries the operator-actionable id.</summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, once it was accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The latest delivery outcome (provider status or a local sentinel).</summary>
    public string Status { get; set; } = string.Empty;

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}

/// <summary>An order in the caller's list, showing where its notifications got to.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
