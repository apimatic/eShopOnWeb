namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionResponse
{
    public bool Created { get; init; }
    public required SubscriptionDto Subscription { get; init; }
}
