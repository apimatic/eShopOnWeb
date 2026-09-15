namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
