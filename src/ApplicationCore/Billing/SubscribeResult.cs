namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscribeResult(CustomerSubscription Subscription, bool Created);
