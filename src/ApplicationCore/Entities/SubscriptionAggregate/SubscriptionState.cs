namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic mirror of the billing provider's subscription lifecycle state.
/// Infrastructure maps the concrete provider's wire values onto this set; ApplicationCore
/// and above never see the provider's own state type.
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
