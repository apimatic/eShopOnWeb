using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>The caller, taken from the JWT — never from the request body.</summary>
    [JsonIgnore]
    public ShopperIdentity? Shopper { get; set; }
}
