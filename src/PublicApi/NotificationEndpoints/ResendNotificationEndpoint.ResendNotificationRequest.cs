namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// The caller-supplied idempotency key. Repeating a request under the same key does not send a second
    /// message; a genuine second attempt uses a fresh key.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
