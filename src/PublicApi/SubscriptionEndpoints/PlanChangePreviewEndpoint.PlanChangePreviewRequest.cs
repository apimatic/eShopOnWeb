namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    /// <summary>The plan to move to, identified by its durable handle.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary>Administrators only: preview for another user.</summary>
    public string UserReference { get; set; }
}
