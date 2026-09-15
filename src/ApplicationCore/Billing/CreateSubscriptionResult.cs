namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CreateSubscriptionResult(ShopperSubscription Subscription, bool Created);
