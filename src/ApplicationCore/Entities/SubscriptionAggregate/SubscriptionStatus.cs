namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a subscription. The billing provider remains the
/// system of record; this enum is the normalized view the rest of eShopOnWeb reasons about.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>The provider reported a state this integration does not recognize.</summary>
    Unknown = 0,

    /// <summary>Created but not yet activated.</summary>
    Pending,

    /// <summary>Inside a trial period.</summary>
    Trialing,

    /// <summary>Live and billing normally.</summary>
    Active,

    /// <summary>Deliberately held by the customer or an admin; resumable.</summary>
    OnHold,

    /// <summary>Held by the provider because the account is in arrears; not customer-resumable.</summary>
    Paused,

    /// <summary>A payment failed and the provider is retrying.</summary>
    PastDue,

    /// <summary>Dunning exhausted without payment.</summary>
    Unpaid,

    /// <summary>Prepaid balance exhausted.</summary>
    Suspended,

    /// <summary>Cancelled; reactivation is possible.</summary>
    Canceled,

    /// <summary>Reached its expiry date.</summary>
    Expired,

    /// <summary>The trial ended without conversion.</summary>
    TrialEnded,

    /// <summary>The provider could not create the subscription.</summary>
    Failed
}
