using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int NotificationId { get; set; }
}
