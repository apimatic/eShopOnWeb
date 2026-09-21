namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is true when an idempotent replay
/// resolved to a subscription that was already present (so no new subscription was created).
/// </summary>
public sealed record SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }

    /// <summary>True when the subscription already existed (idempotent replay), false when newly created.</summary>
    public required bool AlreadyExisted { get; init; }
}
