using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The plan handle to subscribe to (see GET /api/subscription-plans).
    /// </summary>
    [Required]
    public string? PlanHandle { get; set; }
}
