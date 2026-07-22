namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>Taken from the route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>
    /// One of <c>pause</c>, <c>resume</c>, <c>cancel</c>, <c>cancelAtEndOfPeriod</c> or <c>reactivate</c>,
    /// case-insensitive. Accepted as text so an unrecognised value is reported as a validation error rather
    /// than silently binding to the first enum member.
    /// </summary>
    public string Action { get; set; }

    /// <summary>Optional reason recorded with a cancellation.</summary>
    public string Reason { get; set; }
}
