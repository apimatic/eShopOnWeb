using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; }

    /// <summary>
    /// The subscriber's identity, taken from the authenticated token — never from the request body.
    /// </summary>
    [JsonIgnore]
    public string UserName { get; set; }
}
