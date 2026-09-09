using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The caller's identity, taken from the JWT (never from the request body). Set server-side.
    /// </summary>
    [JsonIgnore]
    public string CallerUserName { get; set; } = string.Empty;
}
