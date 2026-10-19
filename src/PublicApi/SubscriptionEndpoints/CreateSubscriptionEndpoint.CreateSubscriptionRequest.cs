namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan from GET /api/subscription-plans; omit to use the configured default plan.</summary>
    public string? PlanHandle { get; set; }
}
