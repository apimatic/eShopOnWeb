namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}
