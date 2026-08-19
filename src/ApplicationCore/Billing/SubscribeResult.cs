namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscribeResult
{
    public ShopperSubscription Subscription { get; set; } = new();
    public bool Created { get; set; }
}
