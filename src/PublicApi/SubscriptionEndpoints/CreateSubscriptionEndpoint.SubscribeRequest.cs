using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as published by <c>GET /api/subscription-plans</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The caller, resolved from the bearer token by the endpoint. Never bound from the request body.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Subscriber? Subscriber { get; set; }
}
