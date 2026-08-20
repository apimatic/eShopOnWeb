namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscribeResult
{
    public required ShopperSubscription Subscription { get; init; }
    public bool Created { get; init; }
}
