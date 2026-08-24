using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
