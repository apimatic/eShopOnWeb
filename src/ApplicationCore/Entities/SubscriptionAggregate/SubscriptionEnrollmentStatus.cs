namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionEnrollmentStatus
{
    /// <summary>Claim taken; the provider has not (yet) confirmed a subscription.</summary>
    Pending = 0,

    /// <summary>The create call failed in a way that leaves its outcome unknown; settled by re-reading the provider.</summary>
    OutcomeUnknown = 1,

    /// <summary>The provider holds the subscription.</summary>
    Completed = 2
}
