using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : SubscriptionEndpointRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes a retry provably safe. Two requests carrying the same key create at
    /// most one subscription. Omitting it is still safe: the caller and plan are used instead, so
    /// an accidental double submit returns the subscription the first one created.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The authenticated caller. Populated on the server from the bearer token; it is deliberately
    /// not deserialized, so a request body cannot name a different subscriber.
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
