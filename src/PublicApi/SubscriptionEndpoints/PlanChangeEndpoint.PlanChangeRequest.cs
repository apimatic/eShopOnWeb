using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>The plan to move to, e.g. "basic-plan".</summary>
    public string? TargetPlanHandle { get; set; }

    /// <summary>Apply now with proration, or at the next renewal without it.</summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediate;

    /// <summary>
    /// The net amount the customer was shown in the preview. When supplied the commit is re-priced
    /// and rejected if it no longer matches.
    /// </summary>
    public decimal? ExpectedNetAmount { get; set; }
}
