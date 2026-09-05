using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to, e.g. "eshop-pro".
    /// Obtain valid values from GET /api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The caller's identity, taken from the JWT rather than the request body so a client
    /// cannot subscribe on another user's behalf.
    /// </summary>
    [JsonIgnore]
    public string CustomerEmail { get; set; } = string.Empty;
}
