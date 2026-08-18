using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The public view of a single notification: what was sent and what became of it. Carries the
/// <see cref="NotificationId"/> the operator endpoints act on and the provider's own identifier and
/// current delivery outcome. The destination number and message text are deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        ContentRedacted = notification.ContentRedacted,
        ProviderMessageId = notification.ProviderMessageId,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt
    };
}
