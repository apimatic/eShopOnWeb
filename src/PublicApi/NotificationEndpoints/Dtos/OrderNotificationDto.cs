using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;

/// <summary>
/// What was sent for an order and what became of it. Carries its own <see cref="NotificationId"/>,
/// which is what the operator endpoints act on. The destination number is never included.
/// </summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The provider's message identifier, once accepted.</summary>
    public string? MessageSid { get; set; }

    /// <summary>The current delivery outcome (provider's own value, or a local sentinel before send).</summary>
    public string DeliveryStatus { get; set; } = string.Empty;

    public int? ErrorCode { get; set; }
    public bool IsFollowUp { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The message text. Null once its content has been disposed of.</summary>
    public string? Body { get; set; }
}
