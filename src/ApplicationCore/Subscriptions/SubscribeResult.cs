namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The shopper's subscription to the requested plan.</param>
/// <param name="AlreadySubscribed">
/// True when the shopper was already enrolled and no new subscription was created. Callers use this to tell a
/// fresh enrollment (201) from an idempotent replay of one (200) — a double-click never bills twice.
/// </param>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadySubscribed);
