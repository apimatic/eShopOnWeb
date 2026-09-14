namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper already held a live subscription to the plan and no new one was created,
/// i.e. this call was a duplicate of an earlier one.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
