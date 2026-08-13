using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.Contracts;

/// <summary>
/// What a caller sees about one notification: its own identifier (which the operator endpoints act
/// on), what it was, and where its delivery got to. The destination number is intentionally omitted.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ProviderMessageId { get; set; }
    public bool IsScheduledFollowUp { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ProviderMessageId = n.ProviderMessageId,
        IsScheduledFollowUp = n.IsScheduledFollowUp,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
