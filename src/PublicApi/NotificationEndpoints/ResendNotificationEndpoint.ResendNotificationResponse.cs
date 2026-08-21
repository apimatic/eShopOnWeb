namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public int SourceNotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
}
