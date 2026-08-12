using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The view of a notification returned to callers. It carries the provider's identifier and current
/// delivery outcome so an operator can act on and report on the message. It deliberately never carries
/// the destination number. <see cref="Body"/> is null once the content has been disposed of.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderSid = n.ProviderSid,
        ErrorCode = n.ErrorCode,
        Body = n.Body,
        ContentRedacted = n.ContentRedacted,
        CreatedDate = n.CreatedDate,
        ScheduledFor = n.ScheduledFor,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
