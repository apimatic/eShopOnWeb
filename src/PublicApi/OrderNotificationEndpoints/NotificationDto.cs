using System;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static NotificationDto From(ApplicationCore.Entities.OrderAggregate.OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Body = n.ContentRedacted ? null : n.Body,
        ContentRedacted = n.ContentRedacted,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedAt = n.CreatedAt,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
