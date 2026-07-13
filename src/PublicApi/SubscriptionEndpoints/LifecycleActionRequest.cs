namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared request shape for the three no-body UC4 lifecycle actions (pause/resume/reactivate).</summary>
public class LifecycleActionRequest : BaseRequest
{
    public LifecycleActionRequest(long subscriptionId, string customerReference, bool actingAsAdmin)
    {
        SubscriptionId = subscriptionId;
        CustomerReference = customerReference;
        ActingAsAdmin = actingAsAdmin;
    }

    public long SubscriptionId { get; }
    public string CustomerReference { get; }
    public bool ActingAsAdmin { get; }
}
