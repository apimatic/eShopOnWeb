namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    internal int NotificationId { get; set; }
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}
