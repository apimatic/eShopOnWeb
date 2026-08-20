using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Direction { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            ProviderSid = notification.ProviderSid,
            Status = notification.Status,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            DateSent = notification.ProviderDateSent,
            DateCreated = notification.ProviderDateCreated,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            Direction = notification.Direction
        };
    }
}
