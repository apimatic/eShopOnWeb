namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper was already enrolled on this plan and no new subscription was created —
/// the idempotent answer to a double-click or a retried request.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
