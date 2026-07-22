namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>The subscription to act on. Taken from the route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>One of <c>Pause</c>, <c>Resume</c>, <c>Cancel</c>, <c>Reactivate</c>.</summary>
    public string Action { get; set; }

    /// <summary>
    /// For <c>Cancel</c> only: <c>Immediate</c> or <c>EndOfPeriod</c>. Defaults to immediate.
    /// </summary>
    public string CancellationTiming { get; set; }

    /// <summary>Optional reason recorded with the transition.</summary>
    public string Reason { get; set; }
}
