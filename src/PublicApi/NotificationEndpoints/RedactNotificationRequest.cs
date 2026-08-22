namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    public RedactNotificationRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}
