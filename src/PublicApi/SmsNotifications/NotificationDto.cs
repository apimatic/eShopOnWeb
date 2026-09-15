using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// What a caller sees about a single message. Deliberately excludes the destination number and the message
/// body — a shopper's number is never exposed. Carries the <see cref="NotificationId"/> the operator
/// endpoints act on, plus enough provider-owned state (SID + delivery status) to report on it.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public bool Scheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto From(Notification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        ProviderMessageSid = notification.ProviderMessageSid,
        Scheduled = notification.IsScheduled,
        ScheduledSendAt = notification.ScheduledSendAt,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}
