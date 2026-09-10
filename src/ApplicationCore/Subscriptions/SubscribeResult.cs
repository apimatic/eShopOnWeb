namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of an enrollment. <see cref="AlreadyExisted"/> is true when an existing live
/// subscription for the same user+plan was found and returned instead of creating a new one —
/// the idempotency guarantee that a double-click never enrolls twice.
/// </summary>
public sealed class SubscribeResult
{
    public required CustomerSubscriptionInfo Subscription { get; init; }
    public required int CustomerId { get; init; }
    public bool AlreadyExisted { get; init; }
    public bool CustomerAlreadyExisted { get; init; }
}
