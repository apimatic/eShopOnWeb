namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <paramref name="AlreadyExisted"/> is <c>true</c> when an
/// existing live subscription was returned instead of creating a new one (idempotent replay).
/// </summary>
public record SubscriptionResult(CustomerSubscription Subscription, bool AlreadyExisted);
