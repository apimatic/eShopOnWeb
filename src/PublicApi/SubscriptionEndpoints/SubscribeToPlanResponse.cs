namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeToPlanResponse
{
    public SubscriptionDto? Subscription { get; set; }

    public bool Created { get; set; }
}
