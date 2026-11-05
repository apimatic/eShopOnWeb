namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to. When omitted the configured default plan is used.</summary>
    public string? PlanHandle { get; set; }
}
