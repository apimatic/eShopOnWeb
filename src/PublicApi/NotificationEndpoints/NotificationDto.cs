using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What a caller sees about a single notification. Carries the provider's identifier and current delivery
/// outcome so operator endpoints can act on it and callers can report on it. The destination number and the
/// message body are deliberately never included.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's message identifier (Twilio SID), once accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's current delivery outcome (e.g. queued/sent/delivered/undelivered/failed/scheduled/canceled).</summary>
    public string? DeliveryStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    /// <summary>Set when the message could not be handed to the provider at all (distinct from a delivery failure).</summary>
    public string? SendError { get; set; }

    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        DeliveryStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        SendError = n.SendError,
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt
    };
}
