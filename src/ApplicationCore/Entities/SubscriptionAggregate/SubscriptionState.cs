namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Provider-agnostic lifecycle state of a <see cref="Subscription"/>.
/// The billing provider's own state name is preserved verbatim on
/// <see cref="Subscription.ProviderState"/>; this enum is the normalized view the
/// domain reasons about.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this integration does not model.</summary>
    Unknown = 0,

    /// <summary>Enrollment has been accepted but has not started billing yet.</summary>
    Pending = 1,

    /// <summary>Billing normally.</summary>
    Active = 2,

    /// <summary>Inside a trial period; treated as active for lifecycle purposes.</summary>
    Trialing = 3,

    /// <summary>An invoice went unpaid; the subscription is in dunning.</summary>
    PastDue = 4,

    /// <summary>Temporarily held; no billing occurs until it is resumed.</summary>
    Paused = 5,

    /// <summary>Cancelled and no longer billing.</summary>
    Canceled = 6,

    /// <summary>Reached its end date, or dunning ran out; no longer billing.</summary>
    Expired = 7
}
