using System.Threading;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Optional: when omitted the
    /// service falls back to the demo default plan, validated against the live product family.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>Resolved from the JWT by the endpoint; not part of the request body.</summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
