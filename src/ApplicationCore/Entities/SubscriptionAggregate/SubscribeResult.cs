namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscribeResult(CustomerSubscription Subscription, bool Created);
