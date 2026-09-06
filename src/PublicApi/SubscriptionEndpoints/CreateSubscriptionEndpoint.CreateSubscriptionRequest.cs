using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrollment request. The subscriber is taken from the bearer token, never from this body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. Required:
    /// handles are stable across catalog re-seeds, numeric plan ids are not.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional first name for the billing customer. eShopOnWeb's identity store holds no personal names,
    /// so one is derived from the caller's email when this is omitted.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the billing customer.</summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Resolved from the caller's access token by the route handler. Excluded from JSON binding so a
    /// request body can never nominate a different subscriber.
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
