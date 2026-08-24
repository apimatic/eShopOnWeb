using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (e.g. a product handle from /api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Resolved server-side from the JWT; never bound from the request body.
    /// </summary>
    [JsonIgnore]
    public ShopperInfo? Shopper { get; set; }
}
