namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRouteRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;

    public ResendNotificationRouteRequest(int notificationId, string idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }
}
