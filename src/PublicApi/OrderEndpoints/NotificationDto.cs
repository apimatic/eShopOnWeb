using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }

    public static NotificationDto FromEntity(OrderNotification n, bool includeBody)
    {
        return new NotificationDto
        {
            NotificationId = n.Id,
            Kind = n.Kind.ToString(),
            Status = n.Status,
            ProviderMessageSid = n.ProviderMessageSid,
            ProviderErrorCode = n.ProviderErrorCode,
            ProviderErrorMessage = n.ProviderErrorMessage,
            CreatedAt = n.CreatedAt,
            ScheduledFor = n.ScheduledFor,
            ContentRedacted = n.ContentRedacted,
            Body = includeBody && !n.ContentRedacted ? n.Body : null
        };
    }
}
