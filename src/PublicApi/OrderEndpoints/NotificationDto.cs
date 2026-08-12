using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The public shape of a notification: enough of the state the provider owns (its identifier and
/// current delivery outcome) to act on and report on it. The destination number is deliberately not
/// exposed — a shopper's number is kept out of responses as well as logs.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Why the message was sent (OrderPlaced, OrderDispatched, DeliveryFollowUp, OrderCancelled, Resend).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message (its message SID), if the provider accepted it.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string? Status { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
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
        CreatedDate = n.CreatedDate,
        LastSyncedAt = n.LastSyncedAt
    };
}
