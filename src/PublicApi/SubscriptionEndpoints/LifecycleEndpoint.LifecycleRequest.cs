namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseMessage
{
    /// <summary>
    /// One of <c>Pause</c>, <c>Resume</c>, <c>CancelImmediately</c>, <c>CancelAtEndOfPeriod</c>,
    /// <c>Reactivate</c>.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Optional reason recorded with the transition.</summary>
    public string? Reason { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    public int SubscriptionId { get; private set; }

    /// <summary>Null for an administrator; otherwise the caller's own reference.</summary>
    public string? RestrictToUserReference { get; private set; }

    public void SetContext(int subscriptionId, string? restrictToUserReference)
    {
        SubscriptionId = subscriptionId;
        RestrictToUserReference = restrictToUserReference;
    }
}
