using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. the product handle of a seeded plan).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The caller's stable reference, taken from the JWT — set server-side, never from the request body.
    /// </summary>
    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;
}
