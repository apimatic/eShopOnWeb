using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key does not send again;
    /// a genuine second attempt uses a fresh key.
    /// </summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}
