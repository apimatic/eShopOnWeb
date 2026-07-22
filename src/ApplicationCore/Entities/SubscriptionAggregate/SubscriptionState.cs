namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing provider is the
/// system of record; this enum is the eShopOnWeb-side vocabulary those states map onto.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this application does not model.</summary>
    Unknown = 0,
    Pending,
    Trialing,
    Active,
    PastDue,
    Suspended,
    Canceled,
    Expired,

    /// <summary>Billing is suspended by an explicit hold and can be resumed.</summary>
    Paused,
    TrialEnded,
    Unpaid,
    Failed
}
