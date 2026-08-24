using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
