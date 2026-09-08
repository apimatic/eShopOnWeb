namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscribeResult
{
    public bool WasAlreadySubscribed { get; init; }

    public SubscriptionDto Subscription { get; init; } = new SubscriptionDto();
}
