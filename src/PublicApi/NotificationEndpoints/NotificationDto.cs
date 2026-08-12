using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent (or queued) for an order and what became of it. Deliberately never carries the
/// destination number or the message text — the shopper's number is not exposed and the body lives
/// only at the provider. The <see cref="NotificationId"/> is what the operator endpoints act on.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>The order event this message was raised for (e.g. OrderPlaced, DeliveryFollowUp).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome (e.g. queued, delivered, undelivered, scheduled).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, when one was issued.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a scheduled follow-up is timed to go out, if this is one.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public bool ContentDisposed { get; set; }

    public bool IsResend { get; set; }
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
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        ContentDisposed = n.ContentDisposed,
        IsResend = n.IsResend,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
