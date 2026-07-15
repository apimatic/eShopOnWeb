namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>The exact preview the customer confirmed — freshly re-derived and compared server-side before committing (UC3 staleness check).</summary>
    public PlanChangePreviewDto ConfirmedPreview { get; set; } = default!;

    public string? OwnerUserId { get; set; }
}
