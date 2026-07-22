using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : AuthenticatedSubscriptionRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>Handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>Immediately (prorated) or at the next renewal (not prorated).</summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediately;

    /// <summary>
    /// The fingerprint of the preview being confirmed. When supplied, the commit is refused if the
    /// basis has moved since the preview was shown. Ignored by the preview endpoint.
    /// </summary>
    public string? Fingerprint { get; set; }
}
