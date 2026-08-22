using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
