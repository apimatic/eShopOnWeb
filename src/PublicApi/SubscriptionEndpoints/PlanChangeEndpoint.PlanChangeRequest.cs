namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>True to apply immediately with proration; false to schedule at next renewal.</summary>
    public bool ApplyNow { get; set; }

    /// <summary>Admin-only: act on a specific subscription instead of the caller's own.
    /// Overwritten by the route handler with the resolved, authorization-checked id.</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>The preview most recently shown to the customer (from <see cref="PreviewPlanChangeEndpoint"/>).
    /// The commit is rejected if a freshly re-run preview no longer matches (UC3 staleness guard).</summary>
    public ProrationPreviewDto ExpectedPreview { get; set; } = default!;
}
