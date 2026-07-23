using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The lifecycle actions a subscription accepts.</summary>
public enum LifecycleAction
{
    Pause = 0,
    Resume,
    Cancel,
    Reactivate
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    public LifecycleAction Action { get; set; }

    /// <summary>Only meaningful for <see cref="LifecycleAction.Cancel"/>.</summary>
    public CancellationTiming Timing { get; set; }

    public string Reason { get; set; }
}
