using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

/// <summary>
/// What was sent for an order and what became of it. Carries the <see cref="NotificationId"/> the operator
/// endpoints act on, plus the provider's message id and current delivery outcome. The destination number
/// is deliberately never exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The current delivery outcome: a provider status once sent, or a local marker before/after.</summary>
    public string Status { get; set; } = string.Empty;

    public int? ErrorCode { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>The provider's message id, once the message was accepted.</summary>
    public string? MessageSid { get; set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; set; }

    public bool ContentRedacted { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the delivery outcome was last refreshed from the provider.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }
}

public static class NotificationDtoMapper
{
    public static NotificationDto ToDto(this OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        FailureReason = n.FailureReason,
        MessageSid = n.MessageSid,
        Body = n.Body,
        ContentRedacted = n.ContentRedacted,
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        CreatedAt = n.CreatedAt,
        LastSyncedAt = n.LastSyncedAt
    };
}
