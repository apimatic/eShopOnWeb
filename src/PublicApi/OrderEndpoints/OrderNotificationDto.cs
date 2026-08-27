using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Body { get; set; }
    public bool IsContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}
