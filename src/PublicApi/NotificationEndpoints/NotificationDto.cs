using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// API view of a notification. Carries the provider's identifier and current delivery
/// outcome so an operator can act on it and report on it. The destination number is masked.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>Masked destination number.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>The message text, or null once its content has been disposed of.</summary>
    public string? Body { get; set; }

    /// <summary>The provider's own message identifier (Twilio SID), if the provider accepted it.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>Current delivery outcome.</summary>
    public string Status { get; set; } = string.Empty;

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        To = NotificationEndpointHelpers.MaskPhoneNumber(n.ToPhoneNumber),
        Body = n.Body,
        ProviderMessageSid = n.ProviderMessageSid,
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };
}
