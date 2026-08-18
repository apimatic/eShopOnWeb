namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public DisposeNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; set; }
}
