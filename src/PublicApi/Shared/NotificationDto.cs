using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.Shared;

/// <summary>
/// The view of a single order notification. Carries the provider's own identifier and its last-known
/// delivery status, so an operator endpoint can act on it and a caller can see where it got to. The
/// recipient number is deliberately never surfaced.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The provider's message identifier (Twilio SID), once accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's fine-grained status verbatim (e.g. delivered, undelivered, scheduled).</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>Coarse classification of where the message got to.</summary>
    public string Outcome { get; set; } = string.Empty;

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }

    /// <summary>The message text (null once its content has been disposed of).</summary>
    public string? Body { get; set; }

    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        Outcome = n.Outcome.ToString(),
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentRedacted = n.ContentRedacted,
        Body = n.Body,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };
}
