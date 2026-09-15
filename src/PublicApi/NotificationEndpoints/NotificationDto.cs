using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The caller-facing view of a notification. Carries the <see cref="NotificationId"/> the operator
/// endpoints act on and the state the provider owns (its message id and current delivery outcome).
/// The destination number is deliberately never included.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }

    /// <summary>Why the message was sent (OrderPlaced, OrderDispatched, OrderCancelled, DeliveryFollowUp, Resend).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The provider's message identifier, once assigned.</summary>
    public string? ProviderMessageSid { get; init; }

    /// <summary>The current delivery outcome (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled, send_error, or not_sent).</summary>
    public string Status { get; init; } = string.Empty;

    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>When the message is/was scheduled with the provider to go out (the delivery follow-up).</summary>
    public DateTimeOffset? ScheduledSendAt { get; init; }

    /// <summary>True once the message text has been disposed of.</summary>
    public bool ContentRedacted { get; init; }

    public DateTimeOffset CreatedDate { get; init; }

    public static NotificationDto From(Notification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        Status = notification.ProviderStatus ?? "not_sent",
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ScheduledSendAt = notification.ScheduledSendAt,
        ContentRedacted = notification.ContentRedacted,
        CreatedDate = notification.CreatedDate
    };
}
