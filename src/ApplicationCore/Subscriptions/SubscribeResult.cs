namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The shopper's subscription to the requested plan.</param>
/// <param name="Created">
/// <c>true</c> when this call enrolled the shopper; <c>false</c> when an equivalent live subscription
/// already existed and was returned instead (the idempotent replay of a double submit).
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool Created);
