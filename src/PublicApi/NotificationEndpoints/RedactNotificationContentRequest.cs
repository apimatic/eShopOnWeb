namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentRequest
{
    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}
