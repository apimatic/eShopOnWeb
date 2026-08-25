using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Resolved from the JWT by the endpoint route; never bound from the request body.
    /// </summary>
    [JsonIgnore]
    public ShopperIdentity? Shopper { get; set; }
}
