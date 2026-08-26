using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.</summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
