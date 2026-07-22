using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : AuthenticatedSubscriptionRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>Pause, Resume, Cancel or Reactivate.</summary>
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>Only meaningful for Cancel: immediately, or at the end of the current period.</summary>
    public CancellationTiming Timing { get; set; } = CancellationTiming.EndOfPeriod;

    public string? Reason { get; set; }
}
