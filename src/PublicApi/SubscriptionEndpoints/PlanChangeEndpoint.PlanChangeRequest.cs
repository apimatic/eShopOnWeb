namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseMessage
{
    /// <summary>Handle of the plan to move to, e.g. <c>basic-plan</c>.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary><c>Immediately</c> (prorated) or <c>AtNextRenewal</c>. Defaults to <c>Immediately</c>.</summary>
    public string? Timing { get; set; }

    /// <summary>
    /// The <c>netAmount</c> returned by the preview. Required when committing: the service re-prices and
    /// rejects the commit if the amount has moved, so the customer is never charged an unshown amount.
    /// </summary>
    public decimal? PreviewedNetAmount { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    public int SubscriptionId { get; private set; }

    /// <summary>Null for an administrator; otherwise the caller's own reference.</summary>
    public string? RestrictToUserReference { get; private set; }

    /// <summary>True when the request arrived on the preview route, so nothing is committed.</summary>
    public bool PreviewOnly { get; private set; }

    public void SetContext(int subscriptionId, string? restrictToUserReference, bool previewOnly)
    {
        SubscriptionId = subscriptionId;
        RestrictToUserReference = restrictToUserReference;
        PreviewOnly = previewOnly;
    }
}
