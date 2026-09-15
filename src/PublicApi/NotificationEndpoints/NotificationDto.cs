using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A notification as returned to a caller: the message id an operator acts on, and where the message got to.
/// The destination number is deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The current delivery outcome (a provider status such as delivered/undelivered, or not_sent).</summary>
    public string? Status { get; set; }

    /// <summary>The provider's identifier for the message.</summary>
    public string? ProviderMessageSid { get; set; }

    public bool Scheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }

    public bool ContentRedacted { get; set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; set; }

    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        Scheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        Body = n.Body,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        CreatedAt = n.CreatedAt,
        LastSyncedAt = n.LastSyncedAt
    };
}
