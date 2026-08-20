namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);
