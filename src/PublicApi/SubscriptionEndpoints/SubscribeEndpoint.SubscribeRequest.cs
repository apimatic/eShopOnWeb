using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    // Populated server-side from the authenticated caller's token; never from the request body.
    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    public string Email { get; set; } = string.Empty;
}
