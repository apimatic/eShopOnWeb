using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public NotificationDto Notification { get; set; } = new();
}
