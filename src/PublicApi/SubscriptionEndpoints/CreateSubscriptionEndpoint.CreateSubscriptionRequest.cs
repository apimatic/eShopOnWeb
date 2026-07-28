using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional price point handle for the plan.</summary>
    public string? PricePointHandle { get; set; }

    /// <summary>
    /// The subscribing user, taken from the JWT on the server side. Ignored during request binding so
    /// it cannot be spoofed by the caller.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
