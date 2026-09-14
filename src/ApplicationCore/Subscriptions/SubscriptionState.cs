using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The lifecycle state of a subscription, mirroring the states defined by the billing provider.
/// </summary>
public enum SubscriptionState
{
    /// <summary>A state that the billing provider reported but this application does not know about.</summary>
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
    /// True when the subscription has run its course and the customer no longer holds it, so
    /// subscribing to the same plan again should create a brand new subscription.
    /// </summary>
    public static bool IsTerminal(this SubscriptionState state) => state switch
    {
        SubscriptionState.Canceled => true,
        SubscriptionState.Expired => true,
        SubscriptionState.FailedToCreate => true,
        SubscriptionState.TrialEnded => true,
        _ => false
    };

    /// <summary>
    /// True when the subscription entitles the customer to the product right now.
    /// </summary>
    public static bool IsLive(this SubscriptionState state) => state switch
    {
        SubscriptionState.Active => true,
        SubscriptionState.Assessing => true,
        SubscriptionState.Pending => true,
        SubscriptionState.Trialing => true,
        SubscriptionState.Paused => true,
        _ => false
    };
}
