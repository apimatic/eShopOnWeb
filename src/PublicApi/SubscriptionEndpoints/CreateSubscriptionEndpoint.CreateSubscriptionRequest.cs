using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// API handle of the plan to subscribe to (e.g. one of the handles returned by
    /// GET /api/subscription-plans).
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
