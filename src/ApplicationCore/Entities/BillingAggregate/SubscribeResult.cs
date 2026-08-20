namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

public class SubscribeResult
{
    public ShopperSubscription Subscription { get; init; } = null!;
    public bool Created { get; init; }
}
