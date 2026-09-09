namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when the caller
/// already had a live subscription to the plan and it was returned instead of creating a second one
/// (the idempotent path a double-click takes).
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
