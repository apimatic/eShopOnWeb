using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The Maxio product handle of the plan being subscribed to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Populated server-side from the caller's JWT identity - never trusted from the client.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public string BuyerEmail { get; set; } = string.Empty;
}
