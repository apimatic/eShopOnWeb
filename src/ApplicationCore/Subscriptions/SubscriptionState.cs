namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Lifecycle state of a subscription in the billing provider.
/// Mirrors the <c>Subscription-State</c> schema of the Maxio Advanced Billing OpenAPI
/// specification (maxio-spec/components/schemas/Subscription-State.yaml).
/// </summary>
public enum SubscriptionState
{
    /// <summary>A state that is not part of the specification this build was compiled against.</summary>
    Unknown = 0,
    Pending,
    FailedToCreate,
    Trialing,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Canceled,
    Expired,
    Paused,
    Unpaid,
    TrialEnded,
    OnHold,
    AwaitingSignup
}

public static class SubscriptionStateExtensions
{
    /// <summary>
    /// True when the state means "the shopper still holds this plan", i.e. the subscription has not
    /// reached a state from which billing will never resume without an explicit new signup.
    /// Used to decide whether a repeated subscribe request is a duplicate of a subscription that
    /// already exists rather than a genuine re-subscribe.
    /// </summary>
    /// <remarks>
    /// <para><c>canceled</c>, <c>expired</c>, <c>trial_ended</c> and <c>failed_to_create</c> are the
    /// end-of-life states after which a shopper may legitimately subscribe to the same plan again.
    /// <c>on_hold</c> and <c>suspended</c> are grouped with the live states because the specification
    /// describes both as temporary stops that are expected to resume.</para>
    /// <para><see cref="SubscriptionState.Unknown"/> — a state this build does not recognise —
    /// counts as current on purpose. Guessing "not current" would let a duplicate subscription be
    /// created and the shopper be billed twice; guessing "current" at worst makes a re-subscribe
    /// return the existing record.</para>
    /// </remarks>
    public static bool IsCurrent(this SubscriptionState state) => state switch
    {
        SubscriptionState.Canceled => false,
        SubscriptionState.Expired => false,
        SubscriptionState.TrialEnded => false,
        SubscriptionState.FailedToCreate => false,
        _ => true
    };
}
