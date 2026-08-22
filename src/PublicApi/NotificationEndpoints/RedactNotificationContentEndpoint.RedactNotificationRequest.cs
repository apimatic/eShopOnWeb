namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}
