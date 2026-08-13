using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What a caller sees about a single notification. It carries the provider's identifier and the
/// current delivery outcome, and the <see cref="NotificationId"/> the operator endpoints act on.
/// The destination number is deliberately not exposed.
/// </summary>
public class SmsNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }

    /// <summary>The provider's identifier for the message (null if the send never reached the provider).</summary>
    public string? ProviderMessageSid { get; set; }

    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SmsNotificationDto From(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ProviderMessageSid = n.ProviderMessageSid,
        ScheduledFor = n.ScheduledFor,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt
    };
}
