using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Resolved server-side from the JWT; never bound from the request.
    /// </summary>
    [JsonIgnore]
    public ShopperInfo? Shopper { get; set; }
}
