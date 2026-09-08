using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated caller to a subscription plan.
/// Identify the plan with planHandle (preferred) or planId.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    [StringLength(200)]
    public string? PlanHandle { get; set; }

    /// <summary>Alternative to planHandle: the billing system's product id.</summary>
    public int? PlanId { get; set; }
}
