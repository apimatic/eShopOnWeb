using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>One of Pause, Resume, Cancel or Reactivate.</summary>
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>For Cancel: true defers to the end of the current period, false cancels now.</summary>
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }
}
