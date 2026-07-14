namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>"Now" (prorated, immediate) or "AtRenewal" (no proration, effective next period).</summary>
    public string Timing { get; set; } = "Now";

    public int SubscriptionId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
