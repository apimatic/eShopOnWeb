namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is true when an existing
/// non-terminal subscription to the same plan was returned instead of creating a new one
/// (idempotent re-subscribe / double-click).
/// </summary>
public record SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }
    public bool AlreadyExisted { get; init; }
}
