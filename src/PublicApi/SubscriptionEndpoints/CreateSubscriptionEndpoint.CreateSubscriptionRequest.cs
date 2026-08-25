using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}
