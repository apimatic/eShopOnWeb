namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscribeToPlanResult
{
    public required ShopperSubscription Subscription { get; init; }
    public bool Created { get; init; }
}
