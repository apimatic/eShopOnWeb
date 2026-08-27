namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key; repeating under the same key does not resend.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
