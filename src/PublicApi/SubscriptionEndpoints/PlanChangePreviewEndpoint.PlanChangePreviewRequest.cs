namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>The durable handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }
}
