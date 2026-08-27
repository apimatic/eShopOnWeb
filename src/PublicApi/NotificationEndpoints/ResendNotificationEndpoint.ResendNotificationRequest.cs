using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key: a repeat under the same key must not send again.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}
