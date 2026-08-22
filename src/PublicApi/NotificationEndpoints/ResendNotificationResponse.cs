namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderSid { get; set; }
}
