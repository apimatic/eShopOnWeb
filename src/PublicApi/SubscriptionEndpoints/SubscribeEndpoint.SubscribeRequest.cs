using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}
