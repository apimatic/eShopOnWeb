namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class CreateSubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
    public bool IsNewSubscription { get; set; }
}
