namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>True to preview the immediate, prorated change; false to preview the change taking
    /// effect at next renewal (no proration).</summary>
    public bool ApplyNow { get; set; }

    /// <summary>Admin-only: preview against a specific subscription instead of the caller's own.
    /// Overwritten by the route handler with the resolved, authorization-checked id.</summary>
    public int? SubscriptionId { get; set; }
}
