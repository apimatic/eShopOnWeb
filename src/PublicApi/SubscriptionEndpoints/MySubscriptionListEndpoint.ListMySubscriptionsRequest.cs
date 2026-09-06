namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Include cancelled and expired subscriptions as well as current ones.</summary>
    public bool IncludeInactive { get; init; }

    public ListMySubscriptionsRequest(bool? includeInactive)
    {
        IncludeInactive = includeInactive ?? false;
    }
}
