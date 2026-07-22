namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>The handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>True to apply now with proration; false to apply at the next renewal.</summary>
    public bool ApplyImmediately { get; set; } = true;

    /// <summary>
    /// The amount the customer confirmed from the preview. When supplied, the commit is rejected
    /// unless a freshly computed preview still yields the same amount.
    /// </summary>
    public decimal? ConfirmedPaymentDue { get; set; }
}
