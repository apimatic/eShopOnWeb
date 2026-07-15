namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;

    // "Now" (prorated, immediate) or "AtNextRenewal" (no proration, deferred).
    public string Timing { get; set; } = "Now";

    // Echoed back from a prior preview call; the commit is rejected if the provider's
    // current proration no longer matches (see UC3's "stale preview" failure scenario).
    public long? ExpectedProratedAdjustmentInCents { get; set; }

    public string? OwnerReference { get; set; }
}
