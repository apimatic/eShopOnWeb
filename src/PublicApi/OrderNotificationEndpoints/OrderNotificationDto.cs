namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public int? OriginalNotificationId { get; set; }
    public string? SendFailure { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static OrderNotificationDto From(ApplicationCore.Entities.NotificationAggregate.OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ScheduledFor = notification.ScheduledFor,
            OriginalNotificationId = notification.OriginalNotificationId,
            SendFailure = notification.SendFailure,
            CreatedAt = notification.CreatedAt
        };
    }
}
