namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key must not send a second
    /// message; a fresh key is a legitimate new attempt. May also be supplied via the
    /// <c>Idempotency-Key</c> request header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
