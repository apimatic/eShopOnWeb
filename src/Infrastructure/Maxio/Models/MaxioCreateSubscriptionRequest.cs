using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>Mirrors the spec's Create Subscription Request envelope.</summary>
[MaxioSchema("Create-Subscription-Request")]
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription? Subscription { get; set; }
}
