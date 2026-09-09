using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for POST /api/subscriptions. The subscriber is taken from the JWT, not the body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    [Required]
    public string? PlanHandle { get; set; }
}
