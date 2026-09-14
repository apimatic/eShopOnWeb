using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan (Maxio product) to subscribe to.</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}
