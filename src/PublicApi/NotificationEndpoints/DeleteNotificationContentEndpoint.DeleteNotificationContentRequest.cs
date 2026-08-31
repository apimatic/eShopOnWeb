namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentRequest : CancellableRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; init; }
}
