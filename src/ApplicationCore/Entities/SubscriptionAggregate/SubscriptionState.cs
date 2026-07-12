namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. Mirrors the billing provider's
/// state vocabulary so <see cref="Interfaces.IBillingClient"/> implementations can map their
/// own wire enum onto this one without leaking provider types into ApplicationCore.
/// </summary>
public enum SubscriptionState
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
    AwaitingSignup
}
