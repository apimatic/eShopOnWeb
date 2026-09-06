using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional. Forwarded to the billing system so that a replay of the same request is
    /// rejected there rather than creating a second subscription. Use a fresh value per
    /// distinct subscribe attempt, for example a UUID generated when the form is rendered.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Resolved from the bearer token by the route, never from the request body.
    /// </summary>
    [JsonIgnore]
    internal Subscriber? Subscriber { get; set; }
}
