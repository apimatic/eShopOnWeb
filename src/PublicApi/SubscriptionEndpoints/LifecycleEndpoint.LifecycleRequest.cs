namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>One of "pause", "resume", "cancel", "reactivate" (case-insensitive).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>For "cancel" only: true = at end of current period, false = immediately.</summary>
    public bool EndOfPeriod { get; set; }
}
