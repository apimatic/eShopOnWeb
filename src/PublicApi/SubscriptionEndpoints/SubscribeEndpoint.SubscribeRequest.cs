using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Body for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The billing subscriber, derived server-side from the authenticated token — never bound
    /// from the request body.
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
