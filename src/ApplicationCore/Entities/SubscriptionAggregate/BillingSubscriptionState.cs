namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. <see cref="Unknown"/> is used when the
/// billing provider reports a state this application does not model, so that an unrecognised
/// state is never mistaken for a terminated one.
/// </summary>
public enum BillingSubscriptionState
{
    Unknown = 0,
    Pending,
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
    FailedToCreate
}
