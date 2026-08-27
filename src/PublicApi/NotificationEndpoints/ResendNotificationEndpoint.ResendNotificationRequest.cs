namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key returns the
    /// message the first attempt produced, without sending again.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
