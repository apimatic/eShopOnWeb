namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared request shape for the UC4 lifecycle actions (pause/resume/reactivate - no extra params).</summary>
public class LifecycleRequest : BaseRequest
{
    public string ActingBuyerId { get; init; }
    public bool IsAdmin { get; init; }
    public int SubscriptionId { get; init; }

    public LifecycleRequest(string actingBuyerId, bool isAdmin, int subscriptionId)
    {
        ActingBuyerId = actingBuyerId;
        IsAdmin = isAdmin;
        SubscriptionId = subscriptionId;
    }
}
