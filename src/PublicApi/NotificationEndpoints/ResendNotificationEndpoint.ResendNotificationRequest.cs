using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    [JsonIgnore]
    public int NotificationId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
