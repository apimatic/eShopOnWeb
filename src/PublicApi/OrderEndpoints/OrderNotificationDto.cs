using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public string? Body { get; set; }
}
