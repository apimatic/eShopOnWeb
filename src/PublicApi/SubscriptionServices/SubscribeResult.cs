namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public class SubscribeResult
{
    public SubscriptionDetailsDto Subscription { get; set; } = new SubscriptionDetailsDto();

    public bool CreatedNew { get; set; }
}
