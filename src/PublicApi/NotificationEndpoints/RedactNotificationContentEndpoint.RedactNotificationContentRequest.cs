namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentRequest : BaseRequest
{
    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}
