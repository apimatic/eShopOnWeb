using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A notification as reported to callers. Deliberately never carries the recipient number.
/// The provider's identifier and current delivery outcome are included so operator endpoints
/// can act on it and callers can see where it got to.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's message identifier (Twilio SID), if the message was accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>Current delivery outcome as the provider records it.</summary>
    public string? Status { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The message text; null once its content has been disposed.</summary>
    public string? Body { get; set; }

    public bool Scheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool Cancelled { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        ProviderMessageSid = notification.ProviderMessageSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.Body,
        Scheduled = notification.IsScheduled,
        ScheduledSendAt = notification.ScheduledSendAt,
        Cancelled = notification.IsCancelled,
        ContentDisposed = notification.ContentDisposed,
        CreatedAt = notification.CreatedAt
    };
}
