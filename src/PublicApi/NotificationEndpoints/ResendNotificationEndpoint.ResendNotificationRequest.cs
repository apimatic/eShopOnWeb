using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key returns the
    /// notification that key produced without sending a second message.
    /// </summary>
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;
}
