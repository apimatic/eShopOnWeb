using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The authenticated shopper's identity, populated server-side from the bearer token.
    /// Any value supplied in the request body is ignored.
    /// </summary>
    [JsonIgnore]
    public string RequesterEmail { get; set; } = string.Empty;
}
