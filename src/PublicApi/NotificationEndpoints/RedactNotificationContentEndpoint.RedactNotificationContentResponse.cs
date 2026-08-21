namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
