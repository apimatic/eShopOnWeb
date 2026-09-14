using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>Request for POST api/subscriptions.</summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}
