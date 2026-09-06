namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The shopper's subscription to the requested plan.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper was already enrolled and no new subscription was created - the caller
/// double-clicked, retried, or simply asked twice.
/// </param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
