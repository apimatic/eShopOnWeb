using System;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    /// <summary>The handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediate</c> (prorated) or <c>AtNextRenewal</c>. Defaults to <c>Immediate</c>.</summary>
    public string Timing { get; set; }

    internal int SubscriptionId { get; private set; }
    internal string ActingUserReference { get; private set; }
    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, string actingUserReference, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        ActingUserReference = actingUserReference;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Parses the requested timing. An unrecognized value is rejected rather than silently
    /// defaulting, because the two options bill very differently.
    /// </summary>
    internal PlanChangeTiming ResolveTiming() => PlanChangeTimingParser.Parse(Timing);
}

/// <summary>Shared parsing for the plan-change timing supplied on the wire.</summary>
internal static class PlanChangeTimingParser
{
    internal static PlanChangeTiming Parse(string? timing)
    {
        if (string.IsNullOrWhiteSpace(timing))
        {
            return PlanChangeTiming.Immediate;
        }

        if (Enum.TryParse<PlanChangeTiming>(timing, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(PlanChangeTiming), parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            $"'{timing}' is not a valid plan-change timing. Use 'Immediate' or 'AtNextRenewal'.",
            nameof(timing));
    }
}
