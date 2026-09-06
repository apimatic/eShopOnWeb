namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Why a subscribe call returned the subscription it did. Lets callers tell a fresh enrolment from a
/// de-duplicated one without comparing timestamps.
/// </summary>
public enum SubscribeOutcome
{
    /// <summary>A new subscription was created in the billing provider.</summary>
    Created = 0,

    /// <summary>The shopper already had a live subscription to this plan; that one was returned.</summary>
    AlreadySubscribed = 1,

    /// <summary>A previous request with the same idempotency key already created this subscription.</summary>
    IdempotentReplay = 2
}
