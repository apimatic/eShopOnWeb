namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// eShopOnWeb's provider-agnostic view of a subscription's lifecycle state. The billing seam maps the
/// provider's own state vocabulary onto these members; anything it does not recognise becomes
/// <see cref="Unknown"/> so an unmapped provider state can never be mistaken for an actionable one.
/// </summary>
public enum SubscriptionState
{
    /// <summary>The provider reported a state this integration does not model.</summary>
    Unknown = 0,

    /// <summary>Enrollment has been accepted but is not yet live.</summary>
    Pending = 1,

    /// <summary>In a trial period.</summary>
    Trialing = 2,

    /// <summary>Live and billing normally.</summary>
    Active = 3,

    /// <summary>A payment has failed but the provider is still retrying.</summary>
    PastDue = 4,

    /// <summary>Dunning has exhausted its retries; the subscription is suspended.</summary>
    Suspended = 5,

    /// <summary>Deliberately held; billing is stopped until it is resumed.</summary>
    Paused = 6,

    /// <summary>Cancelled — either immediately or at the end of the period.</summary>
    Cancelled = 7,

    /// <summary>Reached its end date and will not renew.</summary>
    Expired = 8,

    /// <summary>Enrollment was attempted but the provider never created it.</summary>
    Failed = 9
}
