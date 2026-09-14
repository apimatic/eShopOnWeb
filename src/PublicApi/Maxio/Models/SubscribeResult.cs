namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadySubscribed"/> is <c>true</c> when the shopper
/// already had an active subscription to the plan and no new one was created (the idempotent path,
/// e.g. a double-click) — the <see cref="Subscription"/> is the existing one.
/// </summary>
public record SubscribeResult(SubscriptionDto Subscription, bool AlreadySubscribed);
