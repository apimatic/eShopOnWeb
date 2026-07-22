using System;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>One of <c>Pause</c>, <c>Resume</c>, <c>Cancel</c> or <c>Reactivate</c>.</summary>
    public string Action { get; set; }

    /// <summary>
    /// For <c>Cancel</c> only: <c>Immediate</c> or <c>EndOfPeriod</c>. Defaults to
    /// <c>Immediate</c>.
    /// </summary>
    public string Timing { get; set; }

    /// <summary>An optional reason recorded with the transition.</summary>
    public string Reason { get; set; }

    internal int SubscriptionId { get; private set; }
    internal string ActingUserReference { get; private set; }
    internal CancellationToken CancellationToken { get; private set; }

    internal void Bind(int subscriptionId, string actingUserReference, CancellationToken cancellationToken)
    {
        SubscriptionId = subscriptionId;
        ActingUserReference = actingUserReference;
        CancellationToken = cancellationToken;
    }

    internal LifecycleAction ResolveAction()
    {
        if (Enum.TryParse<LifecycleAction>(Action, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(LifecycleAction), parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            $"'{Action}' is not a valid lifecycle action. Use 'Pause', 'Resume', 'Cancel' or 'Reactivate'.",
            nameof(Action));
    }

    /// <summary>
    /// Parses the cancellation timing. An unrecognized value is rejected rather than defaulting,
    /// because cancelling now and cancelling at the period boundary are very different outcomes
    /// for the customer.
    /// </summary>
    internal CancellationTiming ResolveCancellationTiming()
    {
        if (string.IsNullOrWhiteSpace(Timing))
        {
            return CancellationTiming.Immediate;
        }

        if (Enum.TryParse<CancellationTiming>(Timing, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(CancellationTiming), parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            $"'{Timing}' is not a valid cancellation timing. Use 'Immediate' or 'EndOfPeriod'.",
            nameof(Timing));
    }
}
