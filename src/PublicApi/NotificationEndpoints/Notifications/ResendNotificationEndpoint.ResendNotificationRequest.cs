using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Notifications;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. May also be supplied via the Idempotency-Key header.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Set from the route — never bound from the request body.</summary>
    [JsonIgnore]
    public int NotificationId { get; set; }
}
