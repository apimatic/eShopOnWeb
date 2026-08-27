namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; }
}
