using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Resolved server-side from the JWT.</summary>
    [JsonIgnore]
    public ShopperIdentity? Shopper { get; set; }
}
