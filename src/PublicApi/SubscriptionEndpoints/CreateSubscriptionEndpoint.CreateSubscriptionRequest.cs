using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request to subscribe the authenticated shopper to a plan.</summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
