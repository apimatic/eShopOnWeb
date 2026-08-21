namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe call. <see cref="Created"/> is false when an existing
/// live subscription for the same plan was returned (idempotent replay).
/// </summary>
public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);
