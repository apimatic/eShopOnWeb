namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public int NotificationId { get; set; }
}
