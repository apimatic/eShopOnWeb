using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent for a message and what became of it. Carries the provider's identifier and its current
/// delivery outcome so operator endpoints can act on it. Does not echo the shopper's phone number.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, undelivered, failed, scheduled, canceled), or a local "send_failed".</summary>
    public string? Status { get; set; }

    /// <summary>The provider's identifier for this message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        Body = n.Body,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Response of POST /api/notifications/{id}/resend — carries the id of the message the resend produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string? Status { get; set; }

    /// <summary>False when an existing message under the same idempotency key was returned instead of sending again.</summary>
    public bool MessageSent { get; set; }
}

public class ReconciliationEntryDto
{
    public string? Sid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages both the provider and eShop agree on.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about for the configured sender that eShop has no record of.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}
