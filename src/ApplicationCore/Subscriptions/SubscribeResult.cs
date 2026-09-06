namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a <see cref="SubscribeCommand"/>.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="Created">
/// True when this call enrolled the shopper. False when an equivalent subscription already existed
/// and was returned instead - the idempotent path.
/// </param>
/// <param name="Plan">The plan the subscription is for.</param>
public sealed record SubscribeResult(
    CustomerSubscription Subscription,
    bool Created,
    SubscriptionPlan Plan);
