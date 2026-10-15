namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum SubscriptionEnrollmentStatus
{
    /// <summary>Claimed; the provider write has not been confirmed (in flight, or its outcome is unknown).</summary>
    Pending = 0,

    /// <summary>The provider confirmed the subscription; its id is recorded.</summary>
    Active = 1
}
