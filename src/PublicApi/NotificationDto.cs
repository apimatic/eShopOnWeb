using System;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? DeliveryStatus { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? SourceNotificationId { get; set; }
}
