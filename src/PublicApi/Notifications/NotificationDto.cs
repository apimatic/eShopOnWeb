using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// The API view of a single notification: what was sent, and what became of it. Carries the
/// <see cref="NotificationId"/> the operator endpoints (resend, content disposal) act on, plus the provider's
/// own identifier and current delivery outcome. The destination number is masked — a full contact number is
/// never returned.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string Type { get; init; } = string.Empty;

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, failed, undelivered,
    /// scheduled, canceled, ...), or null when nothing was ever handed to the provider.</summary>
    public string? DeliveryStatus { get; init; }

    /// <summary>The provider's own message identifier — what a later resend/redact/reconcile acts on.</summary>
    public string? ProviderSid { get; init; }

    public bool SendFailed { get; init; }
    public string? FailureReason { get; init; }

    public bool IsScheduled { get; init; }
    public DateTimeOffset? ScheduledSendAt { get; init; }

    public bool ContentDisposed { get; init; }

    public int? ProviderErrorCode { get; init; }
    public string? ProviderErrorMessage { get; init; }

    public string? RecipientMasked { get; init; }

    public DateTimeOffset CreatedDate { get; init; }
    public DateTimeOffset? LastUpdatedDate { get; init; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderSid = n.ProviderSid,
        SendFailed = n.SendFailed,
        FailureReason = n.FailureReason,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        RecipientMasked = PhoneMask.Mask(n.Recipient),
        CreatedDate = n.CreatedDate,
        LastUpdatedDate = n.LastUpdatedDate
    };
}
