using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, e.g. "eshop-pro" (see GET /api/subscription-plans).
    /// </summary>
    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Identity of the caller, populated from the JWT — never from the request body.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
