namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
