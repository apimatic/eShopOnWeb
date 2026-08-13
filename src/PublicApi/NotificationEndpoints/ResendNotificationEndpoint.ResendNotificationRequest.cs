namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a re-send under the same key sends nothing new;
    /// a genuine second attempt uses a fresh key.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
