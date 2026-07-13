namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

// Shared request shape for the simple lifecycle actions (pause / resume / reactivate) that
// take no parameters beyond the subscription and the acting user (UC4).
public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; init; }
    public string UserReference { get; init; } = string.Empty;
    public bool IsAdmin { get; init; }

    public LifecycleRequest(int subscriptionId, string userReference, bool isAdmin)
    {
        SubscriptionId = subscriptionId;
        UserReference = userReference;
        IsAdmin = isAdmin;
    }
}
