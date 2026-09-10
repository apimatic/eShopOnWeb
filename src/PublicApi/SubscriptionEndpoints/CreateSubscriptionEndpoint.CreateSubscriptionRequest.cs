using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>). When
    /// omitted, the default (premium) plan in the configured family is used.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The authenticated subscriber, resolved from the caller's token by the endpoint.
    /// Never bound from the request body.
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
