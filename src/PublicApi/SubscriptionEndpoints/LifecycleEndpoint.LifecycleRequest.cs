namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    // "Pause" | "Resume" | "Cancel" | "Reactivate"
    public string Action { get; set; } = string.Empty;

    // Cancel only: true = at end of period, false = immediate.
    public bool EndOfPeriod { get; set; }

    // Cancel only, optional.
    public string? Reason { get; set; }

    public string? OwnerReference { get; set; }
}
