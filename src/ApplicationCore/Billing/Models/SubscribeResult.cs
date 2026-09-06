namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>What a subscribe attempt actually did.</summary>
public enum SubscribeOutcome
{
    /// <summary>A new subscription was created at the provider.</summary>
    Created = 0,

    /// <summary>
    /// The subscriber was already enrolled (duplicate submit, retry, or replayed idempotency key);
    /// the pre-existing subscription is returned unchanged.
    /// </summary>
    AlreadySubscribed = 1
}

/// <summary>The outcome of a subscribe attempt together with the resulting subscription.</summary>
public sealed record SubscribeResult(SubscribeOutcome Outcome, SubscriberSubscription Subscription);
