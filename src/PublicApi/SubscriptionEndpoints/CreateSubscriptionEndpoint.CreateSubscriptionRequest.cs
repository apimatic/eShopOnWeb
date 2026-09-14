using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set from the caller's JWT after binding - never trust a client-supplied value here.</summary>
    [JsonIgnore]
    public string UserName { get; set; } = string.Empty;

    [JsonIgnore]
    public CancellationToken Ct { get; set; }
}
