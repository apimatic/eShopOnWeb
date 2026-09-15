namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}
