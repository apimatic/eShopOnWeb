using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>Pause, Resume, Cancel or Reactivate.</summary>
    public SubscriptionLifecycleAction Action { get; set; }

    /// <summary>Only meaningful for Cancel: immediately, or at the end of the current period.</summary>
    public CancellationTiming CancellationTiming { get; set; } = CancellationTiming.Immediate;

    public string? Reason { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    [JsonIgnore]
    public long SubscriptionId { get; set; }

    /// <summary>Taken from the bearer token, not the body.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
