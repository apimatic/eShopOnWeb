using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST api/subscriptions</c>.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET api/subscription-plans</c>.
    /// Optional when the deployment configures a default plan.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional given name recorded on the billing customer the first time the shopper subscribes.
    /// When omitted it is derived from the shopper's email address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional family name recorded on the billing customer the first time the shopper subscribes.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>Optional organization recorded on the billing customer.</summary>
    public string? Organization { get; set; }

    /// <summary>
    /// The eShopOnWeb user the subscription belongs to. Taken from the bearer token by the
    /// endpoint and deliberately not bound from the request body, so it cannot be spoofed.
    /// </summary>
    [JsonIgnore]
    public string UserName { get; set; } = string.Empty;
}
