using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The subscriber, resolved server-side from the authenticated caller's token. Never bound from
    /// the request body.
    /// </summary>
    [JsonIgnore]
    public SubscriberInfo? Subscriber { get; set; }
}
