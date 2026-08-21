namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscribeResult(UserSubscription Subscription, bool AlreadySubscribed);
