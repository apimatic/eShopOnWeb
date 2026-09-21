namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Result of a subscribe request. <see cref="AlreadyExisted"/> distinguishes a freshly created
/// subscription from an idempotent hit (the caller already had a subscription for this plan — e.g. a
/// double-click), so the caller can confirm the outcome without a second write ever occurring.
/// </summary>
public record SubscribeOutcome(CustomerSubscription Subscription, bool AlreadyExisted, int CustomerId);
