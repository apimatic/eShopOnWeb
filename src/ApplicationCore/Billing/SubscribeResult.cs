namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <param name="Created">False when the shopper already held this subscription (an idempotent repeat).</param>
public record SubscribeResult(SubscriptionDetails Subscription, bool Created);
