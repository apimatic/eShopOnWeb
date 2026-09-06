namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="AlreadySubscribed">True when the caller already had a live subscription to this plan and
/// nothing new was created. Subscribing twice is not an error — it is the expected result of a double
/// click — so the existing subscription is returned instead.</param>
public sealed record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
