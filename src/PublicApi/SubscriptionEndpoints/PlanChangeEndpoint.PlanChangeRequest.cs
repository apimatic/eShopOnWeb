using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>Handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary>Immediately (prorated) or AtNextRenewal (no proration).</summary>
    public PlanChangeTiming Timing { get; set; }

    /// <summary>
    /// The AmountDue returned by the preview. Ignored by the preview call; required by the commit, which
    /// refuses to apply an amount other than the one the customer was shown.
    /// </summary>
    public decimal ConfirmedAmountDue { get; set; }

    /// <summary>Bound from the route, never from the body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Resolved from the bearer token; null for an administrator.</summary>
    public string? OwnerReference { get; set; }

    /// <summary>False when the request carried no usable identity.</summary>
    public bool IsAuthenticated { get; set; }
}
