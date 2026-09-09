using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for subscribing to a plan.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The authenticated caller's user name (email), set from the JWT by the endpoint — never bound from the
    /// request body. The caller's identity always comes from the token, not the payload.
    /// </summary>
    [JsonIgnore]
    public string? CallerUserName { get; set; }
}
