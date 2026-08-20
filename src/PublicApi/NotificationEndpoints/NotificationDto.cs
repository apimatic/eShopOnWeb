using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? ProviderDateSent { get; set; }
    public string? SendAt { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Status = notification.Status,
        ProviderSid = notification.ProviderSid,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        Body = notification.ContentDisposed ? null : notification.Body,
        ContentDisposed = notification.ContentDisposed,
        CreatedAt = notification.CreatedAt.ToString("O"),
        ProviderDateSent = notification.ProviderDateSent,
        SendAt = notification.SendAt?.ToString("O")
    };
}
