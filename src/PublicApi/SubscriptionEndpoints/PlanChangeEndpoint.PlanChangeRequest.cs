using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>The durable handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediately</c> prorates; <c>AtNextRenewal</c> defers to the period boundary.</summary>
    public PlanChangeTiming Timing { get; set; }

    /// <summary>
    /// The amount previewed and shown to the customer. When supplied it must still match a fresh
    /// preview, otherwise the commit is refused as stale.
    /// </summary>
    public decimal? PreviewedPaymentDue { get; set; }
}
