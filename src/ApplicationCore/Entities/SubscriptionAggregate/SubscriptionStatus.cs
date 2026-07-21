namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic projection of a subscription's lifecycle state. Mirrors every value the
/// billing provider models (so no wire-level state is silently collapsed), without leaking the
/// provider SDK's own enum type into ApplicationCore.
/// </summary>
public enum SubscriptionStatus
{
    Pending,
    AwaitingSignup,
    Trialing,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Paused,
    Unpaid,
    TrialEnded,
    Canceled,
    Expired,
    FailedToCreate,
    Other
}

public static class SubscriptionStatusExtensions
{
    /// <summary>A terminal subscription cannot transition anywhere except by creating a new subscription.</summary>
    public static bool IsTerminal(this SubscriptionStatus status) =>
        status is SubscriptionStatus.Canceled or SubscriptionStatus.Expired or SubscriptionStatus.FailedToCreate;

    public static bool IsPaused(this SubscriptionStatus status) => status == SubscriptionStatus.Paused;

    /// <summary>True for the states in which the subscription is billing normally (used to decide whether usage may be recorded).</summary>
    public static bool IsActiveLike(this SubscriptionStatus status) =>
        status is SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue;

    /// <summary>Reactivation is only a legal transition from these states.</summary>
    public static bool CanReactivate(this SubscriptionStatus status) =>
        status is SubscriptionStatus.Canceled or SubscriptionStatus.Unpaid or SubscriptionStatus.TrialEnded;
}
