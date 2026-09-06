namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt. <paramref name="AlreadySubscribed"/> distinguishes a freshly created
/// subscription from an idempotent replay, so callers can answer 201 vs 200.
/// </summary>
/// <param name="Subscription">The subscription now backing the shopper's enrollment.</param>
/// <param name="AlreadySubscribed">True when the shopper was already enrolled and nothing new was created.</param>
/// <param name="CustomerCreated">True when a billing customer record had to be created for this shopper.</param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed, bool CustomerCreated);
