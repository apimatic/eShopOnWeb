using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan. The shopper's identity is taken from the JWT,
/// not the request body.
/// </summary>
public class SubscribeRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}
