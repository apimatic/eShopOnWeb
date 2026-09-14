using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro"). Required.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Resolved server-side from the caller's JWT; never bound from the request body. Ensures the
    /// subscription is created for the authenticated user, not a client-supplied identity.
    /// </summary>
    [JsonIgnore]
    internal SubscriberIdentity? Subscriber { get; set; }
}
