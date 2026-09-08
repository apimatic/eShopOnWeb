using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".</summary>
    [Required(AllowEmptyStrings = false)]
    public string PlanHandle { get; set; } = string.Empty;
}
