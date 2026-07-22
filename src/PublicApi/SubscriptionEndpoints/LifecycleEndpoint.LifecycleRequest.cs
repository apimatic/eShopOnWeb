using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>The subscription being transitioned. Taken from the route.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>One of Pause, Resume, Cancel or Reactivate.</summary>
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>For Cancel: whether to stop now or at the end of the current period.</summary>
    public CancellationTiming Timing { get; set; } = CancellationTiming.Immediate;

    /// <summary>An optional note recorded against the transition.</summary>
    public string? Reason { get; set; }
}
