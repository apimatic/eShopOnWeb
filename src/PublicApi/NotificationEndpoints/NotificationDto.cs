using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What a caller sees about a single message: the operator handle (<see cref="NotificationId"/>), the
/// provider's identifier and current delivery outcome, and where it got to. The destination number is
/// never included.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery status (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string Status { get; set; } = string.Empty;

    public bool ReachedHandset { get; set; }
    public bool DidNotReach { get; set; }

    public string? ProviderMessageSid { get; set; }
    public string? ProviderErrorCode { get; set; }
    public bool ContentRedacted { get; set; }

    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.ProviderStatus,
        ReachedHandset = MessageDeliveryStatus.ReachedHandset(n.ProviderStatus),
        DidNotReach = MessageDeliveryStatus.DidNotReachHandset(n.ProviderStatus),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ContentRedacted = n.ContentRedacted,
        CreatedDate = n.CreatedDate,
        ScheduledSendAt = n.ScheduledSendAt,
    };
}
