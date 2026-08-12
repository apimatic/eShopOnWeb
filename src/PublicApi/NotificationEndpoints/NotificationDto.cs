using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The API view of a single notification. Deliberately excludes the destination phone number (PII);
/// it carries the provider's identifier and current delivery outcome so an operator can act and
/// report on it.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ProviderMessageSid { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsFollowUp { get; init; }
    public bool ContentDisposed { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ScheduledSendAt { get; init; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsFollowUp = n.IsFollowUp,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        ScheduledSendAt = n.ScheduledSendAt
    };
}
