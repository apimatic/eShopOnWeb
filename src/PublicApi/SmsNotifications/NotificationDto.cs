using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// What a caller sees about one notification message. It carries the <see cref="NotificationId"/>
/// the operator endpoints act on and the current delivery outcome. The recipient number is
/// deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>Current delivery outcome (queued, sent, delivered, undelivered, failed, scheduled, canceled, ...).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Provider error code on a failed/undelivered message, if any.</summary>
    public int? ErrorCode { get; set; }

    /// <summary>The provider's message identifier, if one was issued.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>True once the message content has been disposed of.</summary>
    public bool ContentDisposed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>For a scheduled follow-up, when it is due to go out.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ProviderMessageSid = n.ProviderMessageSid,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor
    };
}
