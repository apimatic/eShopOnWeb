namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper already held this subscription and nothing new was created, i.e. the
/// request was an idempotent replay (a double-click, a retry, or a second browser tab).
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
