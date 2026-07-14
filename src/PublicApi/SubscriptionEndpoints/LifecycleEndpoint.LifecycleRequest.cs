namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    public LifecycleAction Action { get; set; }

    /// <summary>Cancel only: true for end-of-period cancellation, false for immediate.</summary>
    public bool EndOfPeriod { get; set; }

    /// <summary>Cancel only: optional reason recorded with the provider.</summary>
    public string? Reason { get; set; }

    /// <summary>Admin-only: act on a specific subscription instead of the caller's own.
    /// Overwritten by the route handler with the resolved, authorization-checked id.</summary>
    public int? SubscriptionId { get; set; }
}
