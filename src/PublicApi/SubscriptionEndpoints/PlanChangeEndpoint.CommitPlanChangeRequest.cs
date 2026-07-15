namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }
    public int? ExpectedProratedAdjustmentInCents { get; set; }

    /// <summary>Overwritten server-side from the authenticated principal — never trust a client-supplied value.</summary>
    public string UserReference { get; set; } = string.Empty;
}
