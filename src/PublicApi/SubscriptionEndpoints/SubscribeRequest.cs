using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseMessage
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted, the configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
