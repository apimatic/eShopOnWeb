using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c>. Defaults to <c>Immediate</c>.</summary>
    public string Timing { get; set; }

    /// <summary>
    /// The <c>Fingerprint</c> returned by the preview call. Proves which figures were agreed to.
    /// </summary>
    public string ConfirmedFingerprint { get; set; }

    internal int SubscriptionId { get; private set; }
    internal string ActingUserReference { get; private set; }
    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, string actingUserReference, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        ActingUserReference = actingUserReference;
        CancellationToken = cancellationToken;
    }

    internal PlanChangeTiming ResolveTiming() => PlanChangeTimingParser.Parse(Timing);
}
