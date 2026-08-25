using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Resolved from the JWT by the endpoint route; never bound from the request.
    /// </summary>
    [JsonIgnore]
    public ShopperIdentity? Shopper { get; set; }
}
