namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Set from the route.</summary>
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key. A repeat under the same key sends nothing new.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
