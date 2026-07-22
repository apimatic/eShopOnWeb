using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>Taken from the route, never from the request body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>One of <c>pause</c>, <c>resume</c>, <c>cancel</c>, <c>reactivate</c>.</summary>
    public string? Action { get; set; }

    /// <summary>
    /// For <c>cancel</c>: <c>Immediate</c> or <c>EndOfPeriod</c>. Defaults to
    /// <see cref="CancellationTiming.Immediate"/> when omitted.
    /// </summary>
    public string? Timing { get; set; }

    /// <summary>An optional note recorded against the transition.</summary>
    public string? Reason { get; set; }

    public CancellationTiming ResolveCancellationTiming() =>
        Enum.TryParse<CancellationTiming>(Timing, ignoreCase: true, out var timing)
            ? timing
            : CancellationTiming.Immediate;
}
