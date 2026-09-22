using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Resolved from the caller's token at the endpoint boundary — never bound from the request body.
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
