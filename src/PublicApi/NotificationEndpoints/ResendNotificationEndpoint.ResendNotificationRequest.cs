namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key returns the
    /// notification the first attempt produced instead of sending a second message.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
