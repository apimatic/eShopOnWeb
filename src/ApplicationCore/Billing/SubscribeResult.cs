namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);
