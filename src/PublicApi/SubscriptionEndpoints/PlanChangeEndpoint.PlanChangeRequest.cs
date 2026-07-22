namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>Taken from the route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary>
    /// <c>immediate</c> to apply now with proration, or <c>atNextRenewal</c> to defer to the next renewal
    /// without proration. Case-insensitive; defaults to <c>immediate</c>.
    /// </summary>
    public string Timing { get; set; }

    /// <summary>
    /// The prorated adjustment the customer was shown. When supplied it is re-checked against a fresh
    /// preview and the change is rejected if the amount has moved, so no unexpected amount is ever applied.
    /// </summary>
    public decimal? AcknowledgedProratedAdjustment { get; set; }
}
