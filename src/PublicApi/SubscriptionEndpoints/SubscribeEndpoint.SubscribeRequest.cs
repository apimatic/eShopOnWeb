namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to enrol in. Omit to use the configured default plan.</summary>
    public string? PlanHandle { get; set; }
}
