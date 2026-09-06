namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Lifecycle state of a subscription in the billing system of record.
/// Mirrors the states documented by Maxio Advanced Billing; <see cref="Unknown"/> is used when
/// the provider reports a state this build does not yet recognise.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,

    // Live states
    Pending,
    Trialing,
    Assessing,
    Active,
    Paused,

    // Problem states
    PastDue,
    SoftFailure,
    Unpaid,

    // End-of-life states
    Canceled,
    Expired,
    FailedToCreate,
    OnHold,
    Suspended,
    TrialEnded
}

public static class SubscriptionStateExtensions
{
    /// <summary>
    /// True while the customer still holds the subscription, i.e. it has not reached a terminal
    /// state. Used to decide whether a repeat subscribe request is a no-op instead of a second,
    /// separately billed subscription.
    /// </summary>
    /// <remarks>
    /// <see cref="SubscriptionState.Unknown"/> is deliberately treated as live: when a provider
    /// state cannot be interpreted, declining to create another billable subscription is the
    /// safer failure mode.
    /// </remarks>
    public static bool IsLive(this SubscriptionState state) => state switch
    {
        SubscriptionState.Canceled => false,
        SubscriptionState.Expired => false,
        SubscriptionState.FailedToCreate => false,
        SubscriptionState.TrialEnded => false,
        _ => true
    };
}
