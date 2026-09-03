namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when the user was
/// already actively subscribed to the plan and the existing subscription was returned unchanged
/// (the idempotent, "double-click" path) rather than a new one being created.
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
