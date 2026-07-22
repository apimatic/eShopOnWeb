namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a <see cref="Subscription"/>.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The billing provider reported a state this application does not model.</summary>
    Unknown = 0,
    Pending,
    Trialing,
    Active,
    PastDue,
    Suspended,
    /// <summary>Billing is paused (the subscription is on hold) and can be resumed.</summary>
    Paused,
    Canceled,
    Expired,
    Unpaid,
    TrialEnded,
    Failed
}
