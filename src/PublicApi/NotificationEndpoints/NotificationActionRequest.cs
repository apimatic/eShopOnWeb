namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>A route-bound notification identifier for operator actions.</summary>
public class NotificationActionRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public NotificationActionRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}
