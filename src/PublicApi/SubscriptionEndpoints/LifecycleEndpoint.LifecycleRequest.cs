using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>Only meaningful when <see cref="Action"/> is <see cref="SubscriptionLifecycleAction.Cancel"/>.</summary>
    public bool EndOfPeriod { get; set; }

    public string? Reason { get; set; }
}
