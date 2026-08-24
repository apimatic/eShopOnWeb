using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. a product handle from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Resolved server-side from the JWT; never read from the request body.</summary>
    [JsonIgnore]
    public ShopperIdentity? Shopper { get; set; }
}
