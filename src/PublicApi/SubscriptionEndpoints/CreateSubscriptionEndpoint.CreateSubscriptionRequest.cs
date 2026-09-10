using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The resolved caller identity, populated from the JWT by the endpoint (never bound from the
    /// request body).
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
