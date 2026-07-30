using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for <c>POST /api/subscriptions</c>. The caller's identity is taken from the JWT,
/// never from the body — <see cref="CallerName"/> is populated server-side.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>The authenticated caller's login name, set from the token by the endpoint.</summary>
    [JsonIgnore]
    public string? CallerName { get; set; }
}
