using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent for an order and what became of it. <see cref="NotificationId"/> is the identifier
/// the operator endpoints (resend, content disposal) act on.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's message identifier, when one was assigned.</summary>
    public string? MessageSid { get; set; }

    /// <summary>The current delivery outcome known from the provider.</summary>
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }

    /// <summary>True for the "how did delivery go?" follow-up while it is still queued with the provider.</summary>
    public bool Scheduled { get; set; }

    /// <summary>True once the message content has been disposed of.</summary>
    public bool ContentRedacted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        MessageSid = notification.ProviderMessageSid,
        Status = notification.ProviderStatus,
        ErrorCode = notification.ProviderErrorCode,
        Scheduled = notification.IsScheduled,
        ContentRedacted = notification.ContentRedacted,
        CreatedAt = notification.CreatedAt
    };
}
</content>
