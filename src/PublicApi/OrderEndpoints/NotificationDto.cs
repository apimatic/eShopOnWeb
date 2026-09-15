using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What a caller sees about one message: the identifier the operator endpoints act on, the kind of
/// message, and where its delivery got to. Never carries the shopper's number.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery status (or a local sentinel such as <c>not_sent</c>).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's message identifier, once accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The message text that was sent; null once its content has been disposed of.</summary>
    public string? Message { get; set; }

    public bool ContentDisposed { get; set; }
    public bool Scheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ProviderErrorCode,
        ErrorMessage = n.ProviderErrorMessage,
        Message = n.Body,
        ContentDisposed = n.ContentDisposed,
        Scheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        SentAt = n.ProviderSentAt,
        CreatedAt = n.CreatedAt
    };
}
