namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when an
/// active enrollment for the same plan was already present and was returned instead of
/// creating a duplicate (idempotent subscribe).
/// </summary>
public sealed record SubscribeResult(
    CustomerSubscription Subscription,
    bool AlreadyExisted);
