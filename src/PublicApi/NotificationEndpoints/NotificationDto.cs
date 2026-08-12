using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The operator/shopper view of one notification: what it was, what became of it, and the provider
/// state needed to act on it. Deliberately excludes the destination number and the message text.
/// </summary>
public class NotificationDto
{
    // Identifier the operator endpoints act on.
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    public string Kind { get; set; } = string.Empty;

    // Current delivery outcome as owned by the provider (or "send_failed" when no message was created).
    public string Status { get; set; } = string.Empty;

    // Provider message identifier.
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public bool ContentRedacted { get; set; }

    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentRedacted = n.ContentRedacted,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedDate = n.CreatedDate,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
