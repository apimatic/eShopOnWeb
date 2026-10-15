namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan from GET /api/subscription-plans; the configured default plan when omitted.</summary>
    public string? PlanHandle { get; set; }
}
