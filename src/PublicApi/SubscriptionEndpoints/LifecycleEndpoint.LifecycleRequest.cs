namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>Pause, Resume, Cancel, or Reactivate.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Cancel only: "Immediate" (default) or "EndOfPeriod".</summary>
    public string? CancellationTiming { get; set; }

    public string? Reason { get; set; }

    public int SubscriptionId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
