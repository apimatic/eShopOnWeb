using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The API view of a single SMS notification, including the provider state (its identifier and current
/// delivery outcome) an operator endpoint can act on. The destination number is masked.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The current delivery outcome (e.g. queued, sent, delivered, undelivered, failed, scheduled,
    /// canceled, not_sent).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Masked destination number.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, if it was accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        To = CallerExtensions.Mask(n.ToNumber),
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        LastCheckedAt = n.LastCheckedAt
    };
}
