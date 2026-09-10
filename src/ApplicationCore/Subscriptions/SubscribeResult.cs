namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe operation. <see cref="AlreadyExisted"/> is true when an
/// active enrollment in the requested plan was already present and returned as-is
/// (idempotent replay, e.g. a double-click) rather than a new one being created.
/// </summary>
public record SubscribeResult(CustomerSubscription Subscription, bool AlreadyExisted);
