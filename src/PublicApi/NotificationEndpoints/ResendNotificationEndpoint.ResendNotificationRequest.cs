namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key
    /// returns the original outcome without sending a second message.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
