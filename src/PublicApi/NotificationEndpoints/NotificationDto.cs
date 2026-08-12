using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A notification as returned to a caller. Carries its own <see cref="NotificationId"/> — the id the
/// operator endpoints (resend, content disposal) act on — plus the provider's message id and the
/// current delivery outcome.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Which order-lifecycle event this message corresponds to.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Current delivery outcome (provider status, or a local pending/send_failed value).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's message identifier, once the message was accepted.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>Destination number (the caller's own registered number).</summary>
    public string? To { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>When a follow-up is queued with the provider for the future.</summary>
    public DateTimeOffset? ScheduledSendAt { get; set; }

    /// <summary>True once the message content has been disposed of.</summary>
    public bool ContentDisposed { get; set; }

    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageId = n.ProviderMessageId,
        To = n.ToPhoneNumber,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };
}
