using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The four lifecycle transitions of UC4.</summary>
public enum LifecycleAction
{
    Pause = 0,
    Resume = 1,
    Cancel = 2,
    Reactivate = 3
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    public LifecycleAction Action { get; set; }

    /// <summary>Only meaningful for <see cref="LifecycleAction.Cancel"/>.</summary>
    public CancellationTiming Timing { get; set; } = CancellationTiming.EndOfPeriod;

    public string? Reason { get; set; }
}
