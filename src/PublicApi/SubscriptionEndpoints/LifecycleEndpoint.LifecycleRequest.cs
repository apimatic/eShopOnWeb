using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LifecycleAction
{
    Pause,
    Resume,
    Cancel,
    Reactivate
}

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public LifecycleAction Action { get; set; }

    /// <summary>Only meaningful for <see cref="LifecycleAction.Cancel"/>: immediate (false) or end-of-period (true).</summary>
    public bool CancelAtEndOfPeriod { get; set; }

    /// <summary>Only meaningful for <see cref="LifecycleAction.Cancel"/>.</summary>
    public string? Reason { get; set; }
}
