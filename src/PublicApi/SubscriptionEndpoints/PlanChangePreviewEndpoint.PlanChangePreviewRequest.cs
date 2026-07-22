namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>Taken from the route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }
}
