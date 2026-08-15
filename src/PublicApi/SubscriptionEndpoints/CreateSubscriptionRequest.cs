using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to. When omitted, the configured default plan is used.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Server-derived caller identity — set from the JWT, never bound from the request body.</summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
