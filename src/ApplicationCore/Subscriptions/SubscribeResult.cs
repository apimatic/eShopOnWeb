namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyActive"/> is <c>true</c> when the shopper
/// already had a live subscription to the requested plan and no new subscription was created —
/// the flow is idempotent, so a repeated (e.g. double-clicked) request returns the existing one.
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadyActive);
