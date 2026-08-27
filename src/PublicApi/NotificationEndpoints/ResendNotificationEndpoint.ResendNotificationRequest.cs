using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>From the route, not the body.</summary>
    [JsonIgnore]
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied key: repeating the request under the same key must not send a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
