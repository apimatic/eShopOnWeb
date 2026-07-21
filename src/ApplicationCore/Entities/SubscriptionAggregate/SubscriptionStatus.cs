namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic subscription lifecycle state. Values mirror the billing provider's own
/// state machine so nothing is lost in translation, without leaking provider-specific types
/// outside Infrastructure.
/// </summary>
public enum SubscriptionStatus
{
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
    AwaitingSignup,
    Unknown
}
