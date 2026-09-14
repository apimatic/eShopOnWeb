using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;

public class MaxioCreateSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new MaxioCreateSubscription();
}
