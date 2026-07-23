namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a <see cref="Subscription"/>.
/// The billing provider is the system of record; this enum is the normalized eShopOnWeb view of it.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this application does not model.</summary>
    Unknown = 0,

    /// <summary>Created but not yet started.</summary>
    Pending = 1,

    /// <summary>Inside a trial period; still a live subscription.</summary>
    Trialing = 2,

    /// <summary>Live and billing normally.</summary>
    Active = 3,

    /// <summary>Temporarily held; billing is suspended until it is resumed.</summary>
    Paused = 4,

    /// <summary>Live but with an unpaid balance.</summary>
    PastDue = 5,

    /// <summary>Dunning exhausted and the balance was never collected.</summary>
    Unpaid = 6,

    /// <summary>Suspended by the provider.</summary>
    Suspended = 7,

    /// <summary>The trial finished without converting to a paid period.</summary>
    TrialEnded = 8,

    /// <summary>Cancelled; can be reactivated.</summary>
    Canceled = 9,

    /// <summary>Reached its expiry date.</summary>
    Expired = 10,

    /// <summary>The provider failed to create or activate the subscription.</summary>
    Failed = 11
}
