using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>Where a single notification got to — the id operator endpoints act on, plus its outcome.</summary>
public class NotificationStatusDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public bool IsScheduledFollowUp { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationStatusDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentRedacted = n.ContentRedacted,
        IsScheduledFollowUp = n.IsScheduledFollowUp,
        DateSent = n.ProviderDateSent,
        CreatedAt = n.CreatedAt
    };
}
