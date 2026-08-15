namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when the user was
/// already subscribed to the requested plan and the existing subscription was returned unchanged
/// (the idempotent path — e.g. a double-click), and <c>false</c> when a new subscription was created.
/// </summary>
/// <param name="Subscription">The active subscription for the requested plan.</param>
/// <param name="AlreadyExisted">Whether the subscription already existed (idempotent no-op) rather than being newly created.</param>
public record SubscriptionResult(CustomerSubscription Subscription, bool AlreadyExisted);
