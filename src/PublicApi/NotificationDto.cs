using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The caller-facing view of one notification: what the operator endpoints act on and what reports
/// show. It deliberately never carries the destination phone number.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? ProviderMessageSid { get; set; }
    public bool IsScheduled { get; set; }
    public bool ContentRedacted { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        IsScheduled = n.IsScheduled,
        ContentRedacted = n.ContentRedacted,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
