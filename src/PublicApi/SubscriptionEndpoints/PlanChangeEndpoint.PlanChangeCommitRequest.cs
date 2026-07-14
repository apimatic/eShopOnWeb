namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeCommitRequest : BaseRequest
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = "Now";

    /// <summary>The <c>ComparableAmountInCents</c> shown to the customer at preview time — re-verified at commit to reject a stale preview (§UC3).</summary>
    public long PreviewedAmountInCents { get; set; }

    public int SubscriptionId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
