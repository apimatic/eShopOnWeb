namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="Created">
/// <c>true</c> when this call enrolled the shopper; <c>false</c> when an equivalent live subscription
/// already existed and was returned instead (the idempotent path a double-click takes).
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool Created);
