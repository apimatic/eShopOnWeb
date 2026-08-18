using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// What a caller sees about a single message: its own identifier (which the operator endpoints act on),
/// the provider's identifier and latest delivery outcome, and where it is in its lifecycle. The
/// destination number is masked; the full number is never returned.
/// </summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }
    public string? To { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        ProviderSid = n.ProviderSid,
        Status = n.ProviderStatus,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        To = PhoneMask.Mask(n.ToNumber),
        CreatedAt = n.CreatedAt
    };
}
