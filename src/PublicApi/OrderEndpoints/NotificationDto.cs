using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public bool SendFailed { get; set; }
    public string? SendFailureReason { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public int? RelatedNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            ProviderSid = notification.ProviderSid,
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            SendFailed = notification.SendFailed,
            SendFailureReason = notification.SendFailureReason,
            ScheduledSendAt = notification.ScheduledSendAt,
            RelatedNotificationId = notification.RelatedNotificationId,
            CreatedAt = notification.CreatedAt
        };
    }
}
