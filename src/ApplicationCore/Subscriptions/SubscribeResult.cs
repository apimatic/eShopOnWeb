namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The outcome of a subscribe request. <see cref="AlreadySubscribed"/> is true when the buyer was already
/// enrolled in the plan (an idempotent double-click), in which case no new provider write was made.
/// </summary>
public record SubscribeResult(
    SubscriptionSummary Subscription,
    int CustomerId,
    string CustomerReference,
    bool AlreadySubscribed);
