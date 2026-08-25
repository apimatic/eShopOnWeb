using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonIgnore]
    public string? UserName { get; set; }
}
