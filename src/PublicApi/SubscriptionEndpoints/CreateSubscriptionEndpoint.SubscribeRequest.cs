using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The subscriber's identity, taken from the JWT (not from the request body). Populated by the
    /// endpoint before handling, so it is not part of the API contract.
    /// </summary>
    [JsonIgnore]
    public string UserName { get; set; } = string.Empty;
}
